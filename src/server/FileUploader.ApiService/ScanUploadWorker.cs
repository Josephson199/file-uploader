using Amazon.S3;
using Amazon.S3.Model;
using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using nClam;
using System;
using System.Text.Json;

namespace FileUploader.ApiService
{
    public static class JobStatus
    {
        public const string Pending = "pending";
        public const string Processing = "processing";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public record ScanUpload(int UploadId);

    public class ScanUploadWorker : BackgroundService
    {
        private readonly ILogger<ScanUploadWorker> _logger;
        private readonly IAmazonS3 _s3Client;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ClamClient _clamClient;
        private readonly string _clamScanDirectory;
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
            _logger.LogInformation("ScanUploadWorker started with {WorkerId}", _workerId);

            while (!stoppingToken.IsCancellationRequested)
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var job = await TryDequeueJobAsync(db, _workerId.ToString(), stoppingToken);

                if (job == null)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (OperationCanceledException e)
                    {
                        _logger.LogInformation(e, "Operation cancelled, stopping worker");
                        break;
                    }

                    continue;
                }

                try
                {
                    await ProcessJob(db, job, stoppingToken);
                }
                catch (OperationCanceledException e)
                {
                    _logger.LogInformation(e, "Operation cancelled, stopping worker");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job {JobId}", job.Id);
                    job.Status = JobStatus.Failed;
                    job.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
                }
            }

            _logger.LogInformation("ScanUploadWorker stopping with {WorkerId}", _workerId);
        }

        private async Task ProcessJob(AppDbContext db, Job job, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Processing job {JobId} of type {JobType}", job.Id, job.Type);

            ScanUpload payload = JsonSerializer.Deserialize<ScanUpload>(job.Payload!, s_jsonSerializerOptions)!;

            Upload upload = await db.Uploads.Include(u => u.User).SingleAsync(u => u.UploadId == payload.UploadId, stoppingToken);

            var bucketName = S3Contants.BucketName;

            await DownloadFile(
                _s3Client,
                bucket: bucketName,
                key: upload.ObjectFileKey,
                destinationPath: Path.Combine(_clamScanDirectory, upload.OrignalFileName),
                stoppingToken);

            var scanResult = await _clamClient.ScanFileOnServerMultithreadedAsync($"/scan/{upload.OrignalFileName}", stoppingToken);

            upload.ScanReportRaw = scanResult.RawResult;
            upload.VirusDetected = scanResult.Result == ClamScanResults.VirusDetected ? DateTime.UtcNow : null;

            File.Delete(Path.Combine(_clamScanDirectory, upload.OrignalFileName));

            // Move S3 object to scanned folder
            // Destination key uses forward slashes; keep it deterministic.

            var sourceKey = upload.ObjectFileKey;
            var destinationKey = $"uploads/scanned/{upload.User.Sub}/{upload.FileId}";

            _logger.LogInformation("Moving S3 object from {SourceKey} to {DestinationKey} in bucket {Bucket}", sourceKey, destinationKey, bucketName);

            // Copy object to the scanned folder
            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = sourceKey,
                DestinationBucket = bucketName,
                DestinationKey = destinationKey
            };

            await _s3Client.CopyObjectAsync(copyRequest, stoppingToken);

            // Delete the original object
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = sourceKey
            };

            await _s3Client.DeleteObjectAsync(deleteRequest, stoppingToken);

            // Update DB to point to new key and mark job completed
            upload.ObjectFileKey = destinationKey;
            job.Status = JobStatus.Completed;
            job.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(stoppingToken);

        }

        private static async Task DownloadFile(
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

        private static async Task<Job?> TryDequeueJobAsync(AppDbContext db, string workerId, CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var job = await db.Jobs
                .FromSqlRaw($"""
                    SELECT *
                    FROM "Jobs"
                    WHERE "Status" = '{JobStatus.Pending}'
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

            job.Status = JobStatus.Processing;
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
