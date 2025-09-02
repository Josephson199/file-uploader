using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
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

        subApp.UseTus(httpContext => new DefaultTusConfiguration
        {
            UrlPath = "/files",
            Store = new TusS3Store(
                subApp.ApplicationServices.GetRequiredService<ILogger<TusS3Store>>(),
                new tusdotnet.Stores.S3.TusS3StoreConfiguration
                {
                    BucketName = "bucket",
                    FileObjectPrefix = $"uploads"
                },
                subApp.ApplicationServices.GetRequiredService<IAmazonS3>()
            ),
            Events = new tusdotnet.Models.Configuration.Events
            {   
                OnAuthorizeAsync = ctx =>
                {
                    logger.LogTrace("Tus OnAuthorizeAsync: {Intent} {FileId}",
                                    ctx.Intent, ctx.FileId);
                    return Task.CompletedTask;
                },
                OnCreateCompleteAsync = ctx =>
                {
                    logger.LogInformation("Tus OnCreateCompleteAsync: {FileId}",
                                          ctx.FileId);
                    return Task.CompletedTask;
                },
                OnFileCompleteAsync = async ctx =>
                {
                    logger.LogInformation("Tus OnFileCompleteAsync: {FileId}",
                                          ctx.FileId);
                    var file = await ctx.GetFileAsync();
                    var meta = await file.GetMetadataAsync(ctx.CancellationToken);
                    // post‐upload logic here
                }
            }
        });
    }
);

app.MapDefaultEndpoints();

app.Run();
