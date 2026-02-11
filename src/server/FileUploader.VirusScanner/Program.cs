using Amazon.S3;
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

        services.AddHostedService<VirusScanner>();
    })
    .Build();

await host.RunAsync();
