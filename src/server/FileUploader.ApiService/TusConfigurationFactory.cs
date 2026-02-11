using Amazon.S3;
using FileUploader.ApiService;
using FileUploader.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores.S3;

public class S3Contants
{
    public const string BucketName = "bucket";
}

public class TusConfigurationFactory
{
    private readonly ILogger<TusConfigurationFactory> _logger;
    private readonly FileValidator _fileValidator;
    private readonly IOptions<UploadOptions> _uploadOptions;

    public TusConfigurationFactory(ILogger<TusConfigurationFactory> logger, FileValidator fileValidator, IOptions<UploadOptions> uploadOptions)
    {
        _logger = logger;
        _fileValidator = fileValidator;
        _uploadOptions = uploadOptions;
    }

    public DefaultTusConfiguration Create(HttpContext context)
    {
        return new DefaultTusConfiguration
        {
            MaxAllowedUploadSizeInBytes = int.MaxValue,
            Expiration = new SlidingExpiration(TimeSpan.FromDays(7)),
            UrlPath = "/files",
            Store = new TusS3Store(
                context.RequestServices.GetRequiredService<ILogger<TusS3Store>>(),
                new TusS3StoreConfiguration
                {
                    BucketName = S3Contants.BucketName,
                    FileObjectPrefix = GetObjectFilePrefix(context),
                },
                context.RequestServices.GetRequiredService<IAmazonS3>()
            ),
            Events = new Events
            {
                OnAuthorizeAsync = async ctx =>
                {
                    if (_logger.IsEnabled(LogLevel.Trace))
                    {
                        _logger.LogTrace("Tus OnAuthorizeAsync: {Intent} {FileId}",
                                    ctx.Intent, ctx.FileId);
                    }

                    // Require an authenticated user — Token validation happens via the authentication middleware
                    var user = ctx.HttpContext.User;
                    if (user?.Identity?.IsAuthenticated != true)
                    {
                        ctx.FailRequest(System.Net.HttpStatusCode.Unauthorized, "Authentication required");
                        return;
                    }

                    var currentSub = user.FindFirstValue("sub");
                    if (string.IsNullOrWhiteSpace(currentSub))
                    {
                        ctx.FailRequest(System.Net.HttpStatusCode.Unauthorized, "sub claim missing");
                        return;
                    }

                    // Allow creation for authenticated users
                    if (ctx.Intent == IntentType.CreateFile)
                    {
                        _logger.LogDebug("Authorize CreateFile for user {Sub} FileId={FileId}", currentSub, ctx.FileId);
                        return;
                    }

                    // For other intents require ownership. Check persisted Upload first.
                    try
                    {
                        using var scope = ctx.HttpContext.RequestServices.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var upload = await db.Uploads
                            .Include(u => u.User)
                            .SingleOrDefaultAsync(u => u.FileId == ctx.FileId, ctx.CancellationToken);

                        if (upload != null)
                        {
                            var ownerSub = upload.User?.Sub;
                            if (ownerSub != currentSub)
                            {
                                _logger.LogWarning("Unauthorized access attempt by {Sub} on file {FileId} owned by {OwnerSub}", currentSub, ctx.FileId, ownerSub);
                                ctx.FailRequest(System.Net.HttpStatusCode.Forbidden, "You are not the owner of this file");
                                return;
                            }

                            _logger.LogDebug("Authorized {Intent} for owner {Sub} on file {FileId}", ctx.Intent, currentSub, ctx.FileId);
                            return;
                        }

                        // If no Upload exists yet, check UploadCandidate created at create-time.
                        var candidate = await db.UploadCandidates
                            .Include(c => c.OwnerUser)
                            .SingleOrDefaultAsync(c => c.FileId == ctx.FileId, ctx.CancellationToken);

                        if (candidate != null)
                        {
                            if (candidate.OwnerUser?.Sub != currentSub)
                            {
                                _logger.LogWarning("Unauthorized access attempt by {Sub} on candidate {FileId} owned by {OwnerSub}", currentSub, ctx.FileId, candidate.OwnerUser?.Sub);
                                ctx.FailRequest(System.Net.HttpStatusCode.Forbidden, "You are not the owner of this upload");
                                return;
                            }

                            _logger.LogDebug("Authorized {Intent} for candidate owner {Sub} on file {FileId}", ctx.Intent, currentSub, ctx.FileId);
                            return;
                        }

                        // No upload or candidate found — deny.
                        _logger.LogWarning("No Upload or UploadCandidate found for FileId {FileId} — denying access for {Sub}", ctx.FileId, currentSub);
                        ctx.FailRequest(System.Net.HttpStatusCode.Forbidden, "You are not the owner of this upload");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while checking upload owner for file {FileId}", ctx.FileId);
                        ctx.FailRequest(System.Net.HttpStatusCode.Forbidden, "Unable to verify file owner");
                    }
                },

                OnBeforeCreateAsync = _fileValidator.BeforeCreate,

                // Create an UploadCandidate record when the tus file resource is created.
                OnCreateCompleteAsync = async ctx =>
                {
                    _logger.LogInformation("Tus OnCreateCompleteAsync: {FileId}", ctx.FileId);

                    var user = ctx.HttpContext.User;
                    var sub = user.FindFirstValue("sub") ?? string.Empty;

                    try
                    {
                        using var scope = ctx.HttpContext.RequestServices.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        var owner = await db.Users.SingleOrDefaultAsync(u => u.Sub == sub, ctx.CancellationToken);
                        if (owner == null)
                        {
                            owner = new User { Sub = sub };
                            db.Users.Add(owner);
                            await db.SaveChangesAsync(ctx.CancellationToken);
                        }

                        var candidate = new UploadCandidate
                        {
                            FileId = ctx.FileId,
                            OwnerUserId = owner.UserId,
                            ObjectFileKey = CreateObjectFileKey(ctx.HttpContext, ctx.FileId),
                            CreatedAt = DateTimeOffset.UtcNow
                        };

                        db.UploadCandidates.Add(candidate);
                        await db.SaveChangesAsync(ctx.CancellationToken);

                        _logger.LogDebug("Created UploadCandidate for FileId={FileId} OwnerUserId={OwnerUserId}", ctx.FileId, owner.UserId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create UploadCandidate for FileId={FileId}", ctx.FileId);
                    }
                },

                OnFileCompleteAsync = async ctx =>
                {
                    _logger.LogInformation("Tus OnFileCompleteAsync: {FileId}", ctx.FileId);

                    ITusFile file = await ctx.GetFileAsync();

                    Dictionary<string, Metadata> meta = await file.GetMetadataAsync(ctx.CancellationToken);

                    using var scope = ctx.HttpContext.RequestServices.CreateScope();
                    using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var sub = ctx.HttpContext.User.FindFirstValue("sub");
                    var user = await db.Users.SingleAsync(u => u.Sub == sub, ctx.CancellationToken);

                    // Find candidate (created on OnCreateCompleteAsync)
                    var candidate = await db.UploadCandidates
                        .Include(c => c.OwnerUser)
                        .SingleOrDefaultAsync(c => c.FileId == ctx.FileId, ctx.CancellationToken);

                    if (candidate == null)
                    {
                        _logger.LogWarning("No UploadCandidate found for FileId={FileId}. Proceeding but this may indicate a mismatch.", ctx.FileId);
                    }

                    var upload = new Upload
                    {
                        FileId = ctx.FileId,
                        OrignalFileName = meta.ContainsKey("filename") ? meta["filename"].GetString(Encoding.UTF8) ?? "unknown" : "unknown",
                        ObjectFileKey = candidate?.ObjectFileKey ?? CreateObjectFileKey(ctx.HttpContext, ctx.FileId),
                        UploadedAt = DateTimeOffset.UtcNow,
                        User = user,
                    };

                    db.Uploads.Add(upload);

                    // remove candidate if present
                    if (candidate != null)
                    {
                        db.UploadCandidates.Remove(candidate);
                    }

                    await db.SaveChangesAsync(ctx.CancellationToken);

                    var job = new Job
                    {
                        CreatedAt = DateTimeOffset.UtcNow,
                        MaxAttempts = 5,
                        Status = "pending",
                        Type = "virus-scan",
                        Payload = System.Text.Json.JsonDocument.Parse($@"{{
                                ""uploadId"": {upload.UploadId}
                            }}"),
                        Attempts = 0,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };

                    db.Jobs.Add(job);

                    await db.SaveChangesAsync(ctx.CancellationToken);

                    _logger.LogInformation("Created Upload and Job for FileId={FileId} UploadId={UploadId}", ctx.FileId, upload.UploadId);
                }
            }
        };
    }

    private static string CreateObjectFileKey(HttpContext context, string fileId)
    {
        return $"{GetObjectFilePrefix(context)}/{fileId}";
    }

    private static string GetObjectFilePrefix(HttpContext context)
    {
        return $"uploads/temp/{context.User.FindFirstValue("sub")}";
    }
}