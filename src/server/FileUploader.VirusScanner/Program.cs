using Amazon.S3;
using FellowOakDicom;
using FileUploader.Data;
using FileUploader.VirusScanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using nClam;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                context.Configuration.GetConnectionString("postgresdb")));
        
        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new AmazonS3Config
            {
                ServiceURL = context.Configuration["Storage:ServiceUrl"],
                ForcePathStyle = true
            };

            return new AmazonS3Client(
                context.Configuration["Storage:AccessKey"],
                context.Configuration["Storage:SecretKey"], 
                config
            );
        });

        services.AddSingleton(_ =>
        {
            var uri = context.Configuration["ClamAv:Uri"]
                ?? throw new InvalidOperationException("Missing ClamAv:Uri");

            var parsed = new Uri(uri);
            return new ClamClient(parsed.Host, parsed.Port);
        });

        services.AddSingleton<ZipExtractor>();
        services.AddSingleton<DicomFileValidator>();
        services.AddFellowOakDicom();

        services.AddHostedService<VirusScanner>();
    })
    .Build();

DicomSetupBuilder.UseServiceProvider(host.Services);

var logger = host.Services.GetRequiredService<ILogger<Program>>();

var appVersion = host.Services.GetRequiredService<IConfiguration>()["APP_VERSION"] ?? "0.0.1";

using (logger.BeginScope("AppVersionId: {Id}", appVersion))
{
    await host.RunAsync();
}   
