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

                    if (ctx.Intent == IntentType.CreateFile)
                    {
                        string resourceId = ctx.HttpContext.Request.Headers["X-Custom-Header"].Single() ?? string.Empty;

                        // Check if the user is authorized to create files for the specified resource
                        //var userOwnsResource = await ValidateThatUserOwnsResource(user, resourceId);

                        //if (!userOwnsResource)
                        //{
                        //    ctx.FailRequest(System.Net.HttpStatusCode.Forbidden,
                        //                    $"You are not authorized to create files for resource id {resourceId}");
                        //}
                    }
                    else if (ctx.Intent == IntentType.ConcatenateFiles)
                    {
                        // Check if the user is authorized to upload a chunk to the file ctx.FileId
                        // If not, set ctx.FailRequest() with appropriate status code
                    }
                    else if (ctx.Intent == IntentType.GetFileInfo)
                    {
                        // Check if the user is authorized to get info on the file ctx.FileId
                        // If not, set ctx.FailRequest() with appropriate status code
                    }
                    else if (ctx.Intent == IntentType.DeleteFile)
                    {
                        // Check if the user is authorized to delete the file ctx.FileId
                        // If not, set ctx.FailRequest() with appropriate status code
                    }
                    else if (ctx.Intent == IntentType.WriteFile)
                    {
                        // Check if the user is authorized to upload a chunk to the file ctx.FileId
                        // If not, set ctx.FailRequest() with appropriate status code
                    }
                },

                OnBeforeCreateAsync = _fileValidator.BeforeCreate,

                OnCreateCompleteAsync = ctx =>
                {
                    _logger.LogInformation("Tus OnCreateCompleteAsync: {FileId}",
                                          ctx.FileId);
                    return Task.CompletedTask;
                },
                OnFileCompleteAsync = async ctx =>
                {
                    _logger.LogInformation("Tus OnFileCompleteAsync: {FileId}",
                                          ctx.FileId);

                    ITusFile file = await ctx.GetFileAsync();

                    Dictionary<string, Metadata> meta = await file.GetMetadataAsync(ctx.CancellationToken);

                    using var scope = context.RequestServices.CreateScope();
                    using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var user = await db.Users
                        .SingleAsync(u => u.Sub == context.User.FindFirstValue("sub"));

                    var upload = new Upload
                    {
                        FileId = ctx.FileId,
                        OrignalFileName = meta["filename"].GetString(Encoding.UTF8) ?? "unknown",
                        ObjectFileKey = CreateObjectFileKey(context, ctx.FileId),
                        UploadedAt = DateTimeOffset.UtcNow,
                        User = user,
                    };

                    db.Uploads.Add(upload);

                    await db.SaveChangesAsync();

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

                    await db.SaveChangesAsync();
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