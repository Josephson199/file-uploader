using Amazon.S3;
using Amazon.S3.Model;

namespace FileUploader.ApiService
{
    public class VirusScannerWorker : BackgroundService
    {
        private readonly ILogger<VirusScannerWorker> _logger;
        private readonly IAmazonS3 _s3Client;
        private const string BucketName = "bucket";

        public VirusScannerWorker(ILogger<VirusScannerWorker> logger, IAmazonS3 s3Client)
        {
            _logger = logger;
            _s3Client = s3Client;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MyBackgroundService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                ListObjectsV2Response listObjectResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    Prefix = $"uploads/temp/",
                    MaxKeys = 10_000
                }, stoppingToken);

                listObjectResponse.S3Objects?.ForEach(obj =>
                {
                    _logger.LogInformation("Found object: {key} (size: {size} bytes)", obj.Key, obj.Size);
                });

                _logger.LogInformation("Background service is running at: {time}", DateTimeOffset.Now);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            _logger.LogInformation("MyBackgroundService stopping");
        }
    }
}
