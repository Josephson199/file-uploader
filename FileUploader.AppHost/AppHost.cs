using Aspire.Hosting;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var dataPath = Path.Combine(builder.Environment.ContentRootPath, ".minio", "data");

Directory.CreateDirectory(dataPath);

var minio = builder.AddMinioContainer("minio", port: 9000)
    .WithEnvironment("MINIO_ROOT_USER", "admin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "password")
    .WithBindMount(dataPath, "/data");

var apiService = builder.AddProject<Projects.FileUploader_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(minio)
    .WithEnvironment("Storage__ServiceUrl", "http://localhost:9000")
    .WithEnvironment("Storage__AccessKey", "admin")
    .WithEnvironment("Storage__SecretKey", "password");

builder.AddViteApp(name: "file-upload-app", workingDirectory: "../file-upload-app")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithNpmPackageInstallation();

builder.Build().Run();
