using Amazon.S3;
using Amazon.S3.Model;
using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using nClam;
using System.Text.Json;

namespace FileUploader.ApiService
{
    public class ScanUploadWorker : BackgroundService
    {
        private readonly ILogger<ScanUploadWorker> _logger;
        private readonly IAmazonS3 _s3Client;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ClamClient _clamClient;
        private readonly string _clamScanDirectory;
        private const string BucketName = "bucket";
        private readonly Guid _workerId = Guid.NewGuid();
        private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

        public ScanUploadWorker(ILogger<ScanUploadWorker> logger, IAmazonS3 s3Client, IServiceProvider serviceProvider, IConfiguration configuration, ClamClient clamClient)
        {
            _logger = logger;
            _s3Client = s3Client;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            var clamScanDir = _configuration["ClamAv:ScanDirectory"];
            ArgumentException.ThrowIfNullOrWhiteSpace(clamScanDir);
            _clamScanDirectory = clamScanDir;
            _clamClient = clamClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ScanUploadWorker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                //ListObjectsV2Response listObjectResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
                //{
                //    BucketName = BucketName,
                //    Prefix = $"uploads/temp/",
                //    MaxKeys = 10_000
                //}, stoppingToken);

                //listObjectResponse.S3Objects?.ForEach(obj =>
                //{
                //    _logger.LogInformation("Found object: {key} (size: {size} bytes)", obj.Key, obj.Size);
                //});

                //_logger.LogInformation("Background service is running at: {time}", DateTimeOffset.Now);
                var job = await TryDequeueJobAsync(_workerId.ToString(), stoppingToken);

                if (job == null) 
                { 
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); 
                    continue; 
                }

                _logger.LogInformation("Processing job {JobId} of type {JobType}", job.Id, job.Type);

                ScanUpload payload = JsonSerializer.Deserialize<ScanUpload>(job.Payload!, s_jsonSerializerOptions)!;

                await using var scope = _serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var upload = await db.Uploads.SingleAsync(u => u.UploadId == payload.UploadId, stoppingToken);

                await DownloadFileAsync(
                    _s3Client,
                    bucket: BucketName, 
                    key: upload.FileKey, 
                    destinationPath: Path.Combine(_clamScanDirectory, upload.FileName),
                    stoppingToken);

                var scanResult2 = await _clamClient.ScanFileOnServerAsync($"/scan/{upload.FileName}");
            }

            _logger.LogInformation("ScanUploadWorker stopping");
        }

        public record ScanUpload(int UploadId);
        public async Task DownloadFileAsync(
            IAmazonS3 s3,
            string bucket,
            string key,
            string destinationPath,
            CancellationToken ct)
        {
            var request = new GetObjectRequest
            {
                BucketName = bucket,
                Key = key
            };

            using var response = await s3.GetObjectAsync(request, ct);
            await using var responseStream = response.ResponseStream;
            await using var fileStream = File.Create(destinationPath);

            await responseStream.CopyToAsync(fileStream, 81920, ct);
        }

        public async Task<Job?> TryDequeueJobAsync(string workerId, CancellationToken ct)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var job = await db.Jobs
                .FromSqlRaw("""
                    SELECT *
                    FROM "Jobs"
                    WHERE "Status" = 'pending'
                      AND "Type" = 'scan-upload'
                    ORDER BY "Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(ct);


            if (job == null)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            job.Status = "processing";
            job.LockedAt = DateTime.UtcNow;
            job.LockedBy = workerId;
            job.Attempts += 1;
            job.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return job;
        }

    }
}
