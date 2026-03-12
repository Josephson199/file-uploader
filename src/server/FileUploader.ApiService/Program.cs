using Amazon.Runtime;
using Amazon.S3;
using FileUploader.ApiService;
using FileUploader.ApiService.Middlewares;
using FileUploader.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using tusdotnet;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

//TODO: Add /self endpoint

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

/*
dotnet ef migrations add InitialCreate --project .\src\server\FileUploader.Data\ --startup-project .\src\server\FileUploader.DbMigrator\
*/

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddReverseProxy()
        .LoadFromMemory(
            new[]
            {
                new RouteConfig
                {
                    RouteId = "vite",
                    ClusterId = "viteCluster",
                    Match = new RouteMatch { Path = "/{**catch-all}" },
                }
            },
            new[]
            {
                new ClusterConfig
                {
                    ClusterId = "viteCluster",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        { "vite", new DestinationConfig { Address = builder.Configuration["services:file-upload-app:http:0"]! } }
                    }
                }
            }
        );
}

builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("postgresdb")));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Path = "/";
})
.AddOpenIdConnect(options =>
{
    var baseUrl = builder.Configuration["Keycloak:BaseUrl"]!.TrimEnd('/');

    options.Authority = $"{baseUrl}/realms/aspire";
    options.ClientId = builder.Configuration["Keycloak:ClientId"];
    options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = true;

    options.GetClaimsFromUserInfoEndpoint = true;

    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.TokenValidationParameters.RoleClaimType = "roles";

    options.RequireHttpsMetadata = false;
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
builder.Services.Configure<UploadOptions>(
    builder.Configuration.GetSection("Upload"));
builder.Services.AddSingleton<TusConfigurationFactory>();

builder.Services.AddSingleton<EventStream>();
builder.Services.AddHostedService<JobUpdateListener>();

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

app.MapGet("/api/files-list", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
{
    var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (sub is null)
    {
        return Results.InternalServerError();
    }

    var userId = await db.Users
        .Where(u => u.Sub == sub)
        .Select(u => u.UserId)
        .SingleAsync(ct);

    if (userId == 0)
    {
        return Results.InternalServerError();
    }

    var files = await db.Uploads
        .Where(u => u.UserId == userId)
        .OrderByDescending(u => u.CreatedAt)
        .Select(u => new { u.UploadId, u.FileId, u.OrignalFileName, u.CreatedAt })
        .ToArrayAsync(ct);

    return Results.Ok(files);
})
.RequireAuthorization();

JsonSerializerOptions jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

app.MapGet("/api/events", async (HttpContext context, EventStream stream, AppDbContext db, CancellationToken ct) =>
{
    var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    
    if (sub is null)
    {
        return Results.InternalServerError();
    }

    var userId = await db.Users
           .Where(u => u.Sub == sub)
           .Select(u => u.UserId)
           .SingleAsync(ct);

    if (userId == 0)
    {
        return Results.InternalServerError();
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(sub);

    async IAsyncEnumerable<Job> GetJobs()
    {
        await foreach (var message in stream.Subscribe(ct))
        {
            var msg = JsonSerializer.Deserialize<Job>(message, jsonOptions);
            if (msg is not null && msg.Type == "virus-scan")
            {
                var payload = JsonSerializer.Deserialize<VirusScanPayload>(msg.Payload, jsonOptions);

                ArgumentNullException.ThrowIfNull(payload);

                if (payload.UserId == userId)
                {
                    yield return msg;
                }
            }
        }
    }

    try
    {
        return TypedResults.ServerSentEvents(GetJobs(), eventType: "jobs");
    }
    catch (OperationCanceledException)
    {
        return null;
    }
})
.RequireAuthorization();


// Branch ALL requests that start with /files into Tus
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/files"),
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

app.MapGet("/api/login", async ctx =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true)
    {
        ctx.Response.Redirect("/");
        return;
    }

    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
});

app.MapGet("/api/logout", async ctx =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
});

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/api"),
        subApp =>
        {
            subApp.UseRouting();
            subApp.UseEndpoints(endpoints =>
            {
                endpoints.MapReverseProxy();
            });
        });
}

await app.RunAsync();
