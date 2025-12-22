using Aspire.Hosting;
using FileUploader.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using System.Net.Sockets;

var builder = DistributedApplication.CreateBuilder(args);

var dataPath = Path.Combine(builder.Environment.ContentRootPath, ".minio", "data");

Directory.CreateDirectory(dataPath);

var minio = builder.AddMinioContainer("minio", port: 9000)
    .WithEnvironment("MINIO_ROOT_USER", "admin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "password")
    .WithBindMount(dataPath, "/data");

IMinioClient minioClient = new MinioClient()
    .WithEndpoint("localhost", 9000)
    .WithCredentials("admin", "password")
    .WithSSL(false)
    .Build();

builder.Services.AddSingleton(minioClient);

builder.Services.AddHostedService(sp => new MinioBucketInitializer(
       sp.GetRequiredService<IMinioClient>(),
       bucketName: "bucket",
       pollDelay: TimeSpan.FromSeconds(5)));

// TODO move clam to apphost folder and gitignore relevant paths
var clamav = builder.AddContainer("clamav", "clamav/clamav:latest")
    .WithBindMount("../.clam-scan", "/scan")
    .WithBindMount("../.clam", "/var/lib/clamav")
    .WithEndpoint("clam", e =>
    {
        e.TargetPort = 3310;
        e.Port = 3310;
        e.Protocol = ProtocolType.Tcp;
        e.UriScheme = "tcp";
        e.IsExternal = true;
    });


var keycloakDataPath = Path.Combine(builder.Environment.ContentRootPath, ".keycloak", "data");
Directory.CreateDirectory(keycloakDataPath);

var username = builder.AddParameter("admin", value: "admin");
var password = builder.AddParameter("password", value: "password");
var keycloak = builder.AddKeycloak("keycloak", 8080, username, password)
    .WithDataBindMount(keycloakDataPath)
    .WithRealmImport("./.keycloak");

var postgresDataPath = Path.Combine(builder.Environment.ContentRootPath, ".postgres", "data");
Directory.CreateDirectory(postgresDataPath);

var postgres = builder.AddPostgres("postgres", username, password)
     .WithDataBindMount(postgresDataPath);

var postgresdb = postgres.AddDatabase("postgresdb", "postgresdb");

var apiService = builder.AddProject<Projects.FileUploader_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(minio)
    .WithReference(keycloak)
    .WithReference(postgresdb)
    .WaitFor(minio)
    .WaitFor(clamav)
    .WaitFor(keycloak)
    .WaitFor(postgresdb)
    .WithEnvironment("Storage__ServiceUrl", "http://localhost:9000")
    .WithEnvironment("Storage__AccessKey", "admin")
    .WithEnvironment("Storage__SecretKey", "password")
    .WithEnvironment("ClamAv__Uri", () => "tcp://localhost:3310")
    // Keycloak settings consumed by the API via Aspire service discovery
    .WithEnvironment("Authentication__Authority", "http://localhost:8080/realms/aspire")
    .WithEnvironment("Authentication__Audience", "spa-client");

// Add worker project  for background tasks
// Find objects in s3 by tags or path.
// Download them ../clam-scan
// Start clam scan against file
// Mark s3 object as scanned by tags or by moving s3 object to another path.

builder.AddViteApp(name: "file-upload-app", workingDirectory: "../file-upload-app")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithNpmPackageInstallation()
    .WithEnvironment("VITE_MINIO_URL", "http://localhost:9000")
    .WithEnvironment("VITE_CLAMAV_URL", "http://localhost:3310")
    .WithEnvironment("VITE_KEYCLOAK_URL", "http://localhost:8080/realms/aspire");

var aspireApp = builder.Build();

aspireApp.Run();
