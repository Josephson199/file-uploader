using Amazon.Runtime;
using Amazon.S3;
using FileUploader.ApiService;
using FileUploader.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using nClam;
using System.Security.Claims;
using System.Text;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores.S3;
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

/*
dotnet ef migrations add InitialCreate --project .\src\server\FileUploader.Data\ --startup-project .\src\server\FileUploader.ApiService\
*/

builder.Services.AddDbContextPool<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("postgresdb")));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Add JWT bearer authentication using configuration injected by AppHost
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var baseUrl = builder.Configuration["Keycloak:BaseUrl"]?.TrimEnd('/');
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        options.Authority = $"{baseUrl}/realms/aspire";
        options.RequireHttpsMetadata = false; // running locally inside containers
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Keycloak:Audience"]
        };
    });

builder.Services.AddAuthorization();

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
builder.Services.AddHostedService<VirusScannerBackgroundService>();
builder.Services.AddSingleton<TusConfigurationFactory>();
builder.Services.AddSingleton(sp =>
{
    var clamUri = new Uri(builder.Configuration["ClamAv:Uri"]!);

    return new ClamClient(
        clamUri.Host!,
        clamUri.Port);
});


var app = builder.Build();

// TODO: Create middleware for enriching User data from db

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Database.CanConnect())
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogCritical("Could not connect to the database. Ensure that the database is running and the connection string is correct.");
        }
    }

    var s3client = app.Services.GetRequiredService<IAmazonS3>();

    await s3client.EnsureBucketExistsWithRetriesAsync("bucket");

    app.MapOpenApi();
}

// Ensure authentication/authorization middleware is active before request branching that expects an authenticated user
app.UseAuthentication();
app.UseAuthorization();

// Branch ALL requests that start with /files into Tus
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/files"),
    subApp =>
    {
        var logger = subApp.ApplicationServices.GetRequiredService<ILogger<Program>>();

        // Quick debug: log every time we enter the branch
        subApp.Use(async (ctx, next) =>
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Tus branch for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            }

            await next();
        });

        var tusConfigurationFactory = subApp.ApplicationServices.GetRequiredService<TusConfigurationFactory>();

        subApp.UseTus(tusConfigurationFactory.Create);
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

                    _clamClient.MaxStreamSize = long.MaxValue;

                    //var scanResult = await clam.SendAndScanFileAsync(content);

                    var scanResult2 = await _clamClient.ScanFileOnServerAsync("/scan/ccookbook.pdf");

                    if (scanResult2.Result == ClamScanResults.VirusDetected)
                    {
                        Console.WriteLine("Virus!");
                        return;
                    }

                    // Mark file as uploaded in database, send notification, etc.
                }
            }
        };
    }
}