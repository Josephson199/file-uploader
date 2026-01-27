using Amazon.S3;
using FileUploader.ApiService;
using FileUploader.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using nClam;
using System.Security.Claims;
using System.Text;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores.S3;

public class TusConfigurationFactory
{
    private readonly ILogger<TusConfigurationFactory> _logger;
    private readonly FileValidator _fileValidator;
    private readonly ClamClient _clamClient;

    public TusConfigurationFactory(ILogger<TusConfigurationFactory> logger, FileValidator fileValidator, ClamClient clamClient)
    {
        _logger = logger;
        _fileValidator = fileValidator;
        _clamClient = clamClient;
    }

    public DefaultTusConfiguration Create(HttpContext context)
    {
        return new DefaultTusConfiguration
        {
            //MaxAllowedUploadSizeInBytes = 100 * 1024 * 1024 * 10 * 2, // 2gb
            Expiration = new SlidingExpiration(TimeSpan.FromDays(7)),
            UrlPath = "/files",
            Store = new TusS3Store(
                context.RequestServices.GetRequiredService<ILogger<TusS3Store>>(),
                new TusS3StoreConfiguration
                {
                    BucketName = "bucket",
                    FileObjectPrefix = $"uploads/temp/{context.User.FindFirstValue("sub")}",
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

                    foreach (var item in ctx.Metadata)
                    {
                        var k = item.Key;
                        var v = item.Value.GetString(Encoding.UTF8);
                        _logger.LogInformation($"{k} - {v}");
                    }

                    var fileName = ctx.Metadata["filename"];
                    var fileType = ctx.Metadata["filetype"];

                    if (fileName.HasEmptyValue)
                    {


                    }

                    return Task.CompletedTask;
                },
                OnFileCompleteAsync = async ctx =>
                {
                    _logger.LogInformation("Tus OnFileCompleteAsync: {FileId}",
                                          ctx.FileId);
                    var file = await ctx.GetFileAsync();

                    var meta = await file.GetMetadataAsync(ctx.CancellationToken);

                    foreach (var item in meta)
                    {
                        var k = item.Key;
                        var v = item.Value.GetString(Encoding.UTF8);
                        _logger.LogInformation($"{k} - {v}");
                    }

                    var content = await file.GetContentAsync(ctx.CancellationToken);

                    using var scope = context.RequestServices.CreateScope();
                    using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var user = await db.Users
                        .SingleAsync(u => u.Sub == context.User.FindFirstValue("sub"));

                    var upload = new Upload
                    {
                        FileName = meta["filename"].GetString(Encoding.UTF8) ?? "unknown",
                        FileKey = GetFileKey(context, ctx.FileId),
                        UploadedAt = DateTimeOffset.UtcNow,
                        User = user,
                    };

                    db.Uploads.Add(upload);

                    await db.SaveChangesAsync();

                    var job = new Job
                    {
                        CreatedAt = DateTime.UtcNow,
                        MaxAttempts = 5,
                        Status = "pending",
                        Type = "scan-upload",
                        Payload = System.Text.Json.JsonDocument.Parse($@"{{
                                ""uploadId"": {upload.UploadId}
                            }}"),
                        Attempts = 0,
                        UpdatedAt = DateTime.UtcNow
                    };

                    db.Jobs.Add(job);

                    await db.SaveChangesAsync();


                    //_clamClient.MaxStreamSize = long.MaxValue;

                    ////var scanResult = await clam.SendAndScanFileAsync(content);

                    //var scanResult2 = await _clamClient.ScanFileOnServerAsync("/scan/ccookbook.pdf");

                    //if (scanResult2.Result == ClamScanResults.VirusDetected)
                    //{
                    //    Console.WriteLine("Virus!");s
                    //    return;
                    //}

                    // Mark file as uploaded in database, send notification, etc.
                }
            }
        };
    }

    private static string GetFileKey(HttpContext context, string fileId)
    {
        return $"{GetFileObjectPrefix(context)}/{fileId}";
    }

    private static string GetFileObjectPrefix(HttpContext context)
    {
        return $"uploads/temp/{context.User.FindFirstValue("sub")}";
    }
}