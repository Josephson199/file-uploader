using Microsoft.Extensions.DependencyInjection;
using Minio;
using System.Net.Sockets;

var builder = DistributedApplication.CreateBuilder(args);

var minioUser = builder.AddParameter("minio-user", "admin");
var minioPass = builder.AddParameter("minio-pass", "password");
var keycloakUser = builder.AddParameter("keycloak-admin", "admin");
var keycloakPass = builder.AddParameter("keycloak-password", "password");
var postgresUser = builder.AddParameter("postgres-user", "admin");
var postgresPass = builder.AddParameter("postgres-pass", "password");

var minio = builder.AddMinioContainer("s3", port: 9000)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("MINIO_ROOT_USER", minioUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioPass)
    .WithDataVolume("s3-volume");

var clamScanDir = Path.Combine(builder.Environment.ContentRootPath, ".clam-scan");

var clamav = builder.AddContainer("clamav", "clamav/clamav:latest")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithBindMount(clamScanDir, "/scan")
    .WithVolume("clamav-volume", "/var/lib/clamav")
    .WithEndpoint("clam", e =>
    {
        e.TargetPort = 3310;
        e.Port = 3310;
        e.Protocol = ProtocolType.Tcp;
        e.UriScheme = "tcp";
        e.IsExternal = true;
    });

var keycloakRealmFolder = Path.Combine(builder.Environment.ContentRootPath, ".keycloak", "realm");
var keycloak = builder.AddKeycloak("keycloak", 8080, keycloakUser, keycloakPass)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("keycloak-volume")
    .WithRealmImport(keycloakRealmFolder);

var postgres = builder.AddPostgres("postgres", postgresUser, postgresPass, port: 5432)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("postgres-volume");

var postgresDb = postgres.AddDatabase("postgresdb");

var dbMigrator = builder.AddProject<Projects.FileUploader_DbMigrator>("dbmigrator")
    .WithReference(postgresDb);

var api = builder.AddProject<Projects.FileUploader_ApiService>("api")
    .WithHttpHealthCheck("/health")
    .WithReference(minio)
    .WithReference(keycloak)
    .WithReference(postgresDb)
    .WaitFor(minio)
    .WaitFor(clamav)
    .WaitFor(keycloak)
    .WaitFor(postgresDb)
    .WaitFor(dbMigrator)
    .WithEnvironment("Storage__ServiceUrl", minio.GetEndpoint("http"))
    .WithEnvironment("Storage__AccessKey", minioUser)
    .WithEnvironment("Storage__SecretKey", minioPass)
    .WithEnvironment("ClamAv__Uri", "tcp://localhost:3310")
    .WithEnvironment("ClamAv__ScanDirectory", Path.GetFullPath(clamScanDir))
    .WithEnvironment("Keycloak__BaseUrl", keycloak.GetEndpoint("http"))
    .WithEnvironment("Keycloak__Realm", "aspire")
    .WithEnvironment("Keycloak__Audience", "spa-client");

builder.AddViteApp(name: "file-upload-app", workingDirectory: "../../client/file-upload-app")
    .WithReference(api)
    .WaitFor(api)
    .WithNpmPackageInstallation()
    .WithEnvironment("VITE_KEYCLOAK_BASE_URL", keycloak.GetEndpoint("http"))
    .WithEnvironment("VITE_KEYCLOAK_REALM", "aspire");

await builder.Build().RunAsync();
