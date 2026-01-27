using Amazon.Runtime;
using Amazon.S3;
using FileUploader.ApiService;
using FileUploader.ApiService.Middlewares;
using FileUploader.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using nClam;
using System.Security.Claims;
using tusdotnet;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

/*
dotnet ef migrations add InitialCreate --project .\src\server\FileUploader.Data\ --startup-project .\src\server\FileUploader.DbMigrator\
*/

builder.Services.AddDbContextFactory<AppDbContext>(opt =>
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
        options.MapInboundClaims = false;
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
builder.Services.AddHostedService<VirusScannerWorker>();
builder.Services.AddHostedService<ScanUploadWorker>();
builder.Services.AddSingleton<TusConfigurationFactory>();
builder.Services.AddSingleton(sp =>
{
    var clamUri = new Uri(builder.Configuration["ClamAv:Uri"]!);

    return new ClamClient(
        clamUri.Host!,
        clamUri.Port);
});


var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!await db.Database.CanConnectAsync())
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical("Could not connect to the database. Ensure that the database is running and the connection string is correct.");
    }
}

var s3client = app.Services.GetRequiredService<IAmazonS3>();

await s3client.EnsureBucketExistsWithRetriesAsync("bucket");

app.MapOpenApi();


app.UseAuthentication();
app.UseMiddleware<EnsureUserExistsMiddleware>();
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

app.MapDefaultEndpoints();

await app.RunAsync();
