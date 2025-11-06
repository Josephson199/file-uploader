using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.AppHost
{
    public class MinioBucketInitializer : BackgroundService
    {
        private readonly IMinioClient _client;
        private readonly ILogger<MinioBucketInitializer> _logger;
        private readonly string _bucketName;
        private readonly TimeSpan _pollDelay;

        public MinioBucketInitializer(IMinioClient client, string bucketName, TimeSpan pollDelay, ILogger<MinioBucketInitializer>? logger = null)
        {
            _client = client;
            _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
            _pollDelay = pollDelay;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MinioBucketInitializer>.Instance;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MinioBucketInitializer starting for bucket {Bucket}.", _bucketName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Quick readiness check: ListBucketsAsync will fail if MinIO not reachable
                    await _client.ListBucketsAsync(stoppingToken);

                    // Check bucket existence
                    bool exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucketName), stoppingToken);
                    if (exists)
                    {
                        _logger.LogInformation("Bucket {Bucket} already exists. Initialization complete.", _bucketName);
                        return; // stop the hosted service
                    }

                    // Create bucket
                    _logger.LogInformation("Bucket {Bucket} not found. Creating...", _bucketName);
                    await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName), stoppingToken);
                    _logger.LogInformation("Bucket {Bucket} created successfully.", _bucketName);
                    return; // stop the hosted service
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Stopping MinioBucketInitializer due to cancellation.");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Minio not ready or bucket operation failed. Retrying in {Seconds}s...", _pollDelay.TotalSeconds);
                    try
                    {
                        await Task.Delay(_pollDelay, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Stopping MinioBucketInitializer due to cancellation during delay.");
                        throw;
                    }
                }
            }
        }
    }
}
