using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Util;
using FileUploader.ApiService;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text;
using tusdotnet;
using tusdotnet.Constants;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores.S3;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var cfg = new AmazonS3Config
    {
        ServiceURL = builder.Configuration["Storage:ServiceUrl"],
        ForcePathStyle = true
    };
    var creds = new BasicAWSCredentials(
        builder.Configuration["Storage:AccessKey"],
        builder.Configuration["Storage:SecretKey"]
    );
    return new AmazonS3Client(creds, cfg);
});

builder.Services.AddSingleton<FileValidator>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Branch ALL requests that start with /files into Tus
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/files"),
    subApp =>
    {
        var logger = subApp.ApplicationServices.GetRequiredService<ILogger<Program>>();

        // Quick debug: log every time we enter the branch
        subApp.Use(async (ctx, next) =>
        {
            logger.LogTrace("Tus branch for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await next();
        });

        var fileValidator = subApp.ApplicationServices.GetRequiredService<FileValidator>();

        subApp.UseTus(httpContext => new DefaultTusConfiguration
        {
            //MaxAllowedUploadSizeInBytes = 100 * 1024 * 1024 * 10 * 2, // 2gb
            Expiration = new SlidingExpiration(TimeSpan.FromDays(7)),
            UrlPath = "/files",
            Store = new TusS3Store(
                subApp.ApplicationServices.GetRequiredService<ILogger<TusS3Store>>(),
                new TusS3StoreConfiguration
                {
                    BucketName = "bucket",
                    FileObjectPrefix = $"uploads",
                },
                subApp.ApplicationServices.GetRequiredService<IAmazonS3>()
            ),
            Events = new Events
            {
                OnAuthorizeAsync = async ctx =>
                {
                    logger.LogTrace("Tus OnAuthorizeAsync: {Intent} {FileId}",
                                    ctx.Intent, ctx.FileId);

                    if (ctx.Intent == IntentType.CreateFile)
                    {
                        string resourceId = ctx.HttpContext.Request.Headers["X-Custom-Header"].Single() ?? string.Empty;

                        // Check if the user is authorized to create files for the specified resource
                        var user = ctx.HttpContext.User;

                        var userOwnsResource = await ValidateThatUserOwnsResource(user, resourceId);

                        if (!userOwnsResource)
                        {
                            ctx.FailRequest(System.Net.HttpStatusCode.Forbidden,
                                            $"You are not authorized to create files for resource id {resourceId}");
                        }
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

                OnBeforeCreateAsync = fileValidator.BeforeCreate,
                
                OnCreateCompleteAsync = ctx =>
                {
                    logger.LogInformation("Tus OnCreateCompleteAsync: {FileId}",
                                          ctx.FileId);

                    foreach (var item in ctx.Metadata)
                    {
                        var k = item.Key;
                        var v = item.Value.GetString(Encoding.UTF8);
                        logger.LogInformation($"{k} - {v}");
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
                    logger.LogInformation("Tus OnFileCompleteAsync: {FileId}",
                                          ctx.FileId);
                    var file = await ctx.GetFileAsync();

                    var meta = await file.GetMetadataAsync(ctx.CancellationToken);

                    foreach (var item in meta)
                    {
                        var k = item.Key;
                        var v = item.Value.GetString(Encoding.UTF8);
                        logger.LogInformation($"{k} - {v}");
                    }

                    // Mark file as uploaded in database, send notification, etc.
                }
            }
        });
    }
);

async Task<bool> ValidateThatUserOwnsResource(ClaimsPrincipal user, string resourceId)
{
    // Look up user in db.
    // Validate that user has access to resourceId.
    return true;
}

app.MapDefaultEndpoints();

app.Run();
