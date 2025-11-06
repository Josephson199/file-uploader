using Aspire.Hosting;
using Aspire.Hosting;
using FileUploader.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Projects;
using System.IO;

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

builder.Services.AddSingleton<IMinioClient>(minioClient);

builder.Services.AddHostedService(sp => new MinioBucketInitializer(
       sp.GetRequiredService<IMinioClient>(),
       bucketName: "bucket",
       pollDelay: TimeSpan.FromSeconds(5)));

var apiService = builder.AddProject<Projects.FileUploader_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(minio)
    .WaitFor(minio)
    .WithEnvironment("Storage__ServiceUrl", "http://localhost:9000")
    .WithEnvironment("Storage__AccessKey", "admin")
    .WithEnvironment("Storage__SecretKey", "password");

builder.AddViteApp(name: "file-upload-app", workingDirectory: "../file-upload-app")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithNpmPackageInstallation();

var aspireApp = builder.Build();

aspireApp.Run();
