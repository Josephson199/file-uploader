namespace FileUploader.ApiService
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.S3;
    using Amazon.S3.Util;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    public class S3BucketInitializer : BackgroundService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly ILogger _logger;
        private const string BucketName = "bucket";

        public S3BucketInitializer(IAmazonS3 s3Client, ILogger<S3BucketInitializer> logger)
        {
            _s3Client = s3Client;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("S3BucketInitializer starting up...");

            try
            {
                var exists = await AmazonS3Util
                    .DoesS3BucketExistV2Async(_s3Client, BucketName);

                if (exists)
                {
                    _logger.LogInformation("Bucket {bucket} already exists.", BucketName);
                }
                else
                {
                    _logger.LogInformation("Bucket {bucket} not found. Creating...", BucketName);

                    await _s3Client.PutBucketAsync(new Amazon.S3.Model.PutBucketRequest
                    {
                        BucketName = BucketName,
                        UseClientRegion = true
                    }, stoppingToken);

                    _logger.LogInformation("Bucket {bucket} created successfully.", BucketName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure bucket {bucket}", BucketName);
            }
        }
    }

}
