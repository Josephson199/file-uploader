using Amazon.S3;
using Amazon.S3.Model;
using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using nClam;
using System;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FileUploader.VirusScanner;

public class VirusScanner : BackgroundService
{
    private readonly ILogger<VirusScanner> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly IServiceProvider _serviceProvider;
    private readonly ClamClient _clamClient;
    private readonly string _clamScanDirectory;
    private readonly Guid _workerId = Guid.NewGuid();
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public VirusScanner(
        ILogger<VirusScanner> logger,
        IAmazonS3 s3Client,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ClamClient clamClient)
    {
        _logger = logger;
        _s3Client = s3Client;
        _serviceProvider = serviceProvider;
        _clamClient = clamClient;

        _clamScanDirectory = configuration["ClamAv:ScanDirectory"]
            ?? throw new InvalidOperationException("ClamAv:ScanDirectory is missing");

        Directory.CreateDirectory(_clamScanDirectory);

        _logger.LogInformation("VirusScanner constructed. WorkerId={WorkerId}, ScanDirectory={ScanDir}", _workerId, _clamScanDirectory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VirusScanner started with {WorkerId}", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                _logger.LogDebug("Attempting to dequeue job (worker {WorkerId})", _workerId);
                var job = await TryDequeueJobAsync(db, _workerId.ToString(), stoppingToken);

                if (job == null)
                {
                    _logger.LogTrace("No job found. Sleeping 5s (worker {WorkerId})", _workerId);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Dequeued job {JobId} (attempts={Attempts})", job.Id, job.Attempts);
                await ProcessJobSafe(db, job, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation cancelled, exiting loop (worker {WorkerId})", _workerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in worker loop (worker {WorkerId})", _workerId);
            }
        }

        _logger.LogInformation("VirusScanner stopping with {WorkerId}", _workerId);
    }

    private async Task ProcessJobSafe(AppDbContext db, Job job, CancellationToken ct)
    {
        try
        {
            await ProcessJob(db, job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", job.Id);

            job.Status = JobStatus.Failed;
            job.UpdatedAt = DateTime.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Marked job {JobId} as failed in DB", job.Id);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save failed status for job {JobId}", job.Id);
            }
        }
    }

    private async Task ProcessJob(AppDbContext db, Job job, CancellationToken ct)
    {
        _logger.LogInformation("Processing job {JobId}", job.Id);

        _logger.LogDebug("Deserializing payload for job {JobId}", job.Id);
        var payload = JsonSerializer.Deserialize<VirusScanPayload>(job.Payload!, s_jsonSerializerOptions)
            ?? throw new InvalidOperationException("Invalid job payload");
        _logger.LogDebug("Payload deserialized for job {JobId}: UploadId={UploadId}", job.Id, payload.UploadId);

        _logger.LogDebug("Loading upload record UploadId={UploadId}", payload.UploadId);
        var upload = await db.Uploads
            .Include(u => u.User)
            .SingleOrDefaultAsync(u => u.UploadId == payload.UploadId, ct);

        if (upload == null)
        {
            _logger.LogWarning("Upload {UploadId} not found for job {JobId}", payload.UploadId, job.Id);
            throw new InvalidOperationException($"Upload {payload.UploadId} not found");
        }
        _logger.LogInformation("Found upload {UploadId} (FileId={FileId}, OriginalName={OriginalName})", upload.UploadId, upload.FileId, upload.OrignalFileName);

        var bucketName = "bucket";

        // Local file path (ZIP or non-ZIP)
        var localPath = Path.Combine(_clamScanDirectory, upload.FileId);
        _logger.LogDebug("Local path for download: {LocalPath}", localPath);

        // Temp extraction directory (only used for ZIPs)
        var extractDir = Path.Combine(_clamScanDirectory, $"{upload.FileId}_extract");
        _logger.LogDebug("Extract directory (if needed): {ExtractDir}", extractDir);

        try
        {
            // 1. Download file from S3
            _logger.LogInformation("Downloading S3 object {Bucket}/{Key} to {LocalPath}", bucketName, upload.ObjectFileKey, localPath);
            await DownloadFile(_s3Client, bucketName, upload.ObjectFileKey, localPath, ct);
            _logger.LogInformation("Downloaded S3 object to {LocalPath}", localPath);

            FileInfo li = new FileInfo(localPath);
            _logger.LogDebug("Downloaded file size: {SizeBytes} bytes", li.Exists ? li.Length : -1);

            bool isZip = upload.OrignalFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("Is zip: {IsZip} for file {FileName}", isZip, upload.OrignalFileName);

            if (isZip)
            {
                // ZIP-specific scanning flow
                _logger.LogInformation("ZIP detected. Creating extract directory {ExtractDir}", extractDir);
                Directory.CreateDirectory(extractDir);

                _logger.LogDebug("Extracting ZIP {LocalPath}", localPath);
                var extractedFiles = await ExtractAndValidateZipAsync(localPath, extractDir, ct);
                _logger.LogInformation("Extracted {Count} files from ZIP {LocalPath}", extractedFiles.Count, localPath);

                _logger.LogDebug("Validating {Count} extracted files as DICOM", extractedFiles.Count);
                ValidateDicomFiles(extractedFiles);
                _logger.LogInformation("DICOM validation passed for {Count} files", extractedFiles.Count);

                // Scan ZIP container only
                _logger.LogInformation("Sending file {FileId} to ClamAV for scanning (ZIP container)", upload.FileId);
                var scanResult = await _clamClient.ScanFileOnServerMultithreadedAsync($"/scan/{upload.FileId}", ct);
                _logger.LogInformation("ClamAV scan completed for {FileId}. Result={Result}", upload.FileId, scanResult.Result);
                _logger.LogDebug("ClamAV raw result length: {Len}", scanResult.RawResult?.Length ?? 0);

                upload.ScanReportRaw = scanResult.RawResult.Replace("\0", "");
                upload.VirusDetected = scanResult.Result == ClamScanResults.VirusDetected ? DateTimeOffset.UtcNow : null;
            }
            else
            {
                // Non-ZIP: direct ClamAV scan
                _logger.LogInformation("Sending file {FileId} to ClamAV for scanning (non-zip)", upload.FileId);
                var scanResult = await _clamClient.ScanFileOnServerMultithreadedAsync($"/scan/{upload.FileId}", ct);
                _logger.LogInformation("ClamAV scan completed for {FileId}. Result={Result}", upload.FileId, scanResult.Result);
                _logger.LogDebug("ClamAV raw result length: {Len}", scanResult.RawResult?.Length ?? 0);

                upload.ScanReportRaw = scanResult.RawResult;
                upload.VirusDetected = scanResult.Result == ClamScanResults.VirusDetected ? DateTimeOffset.UtcNow : null;
            }

            _logger.LogDebug("Updating upload entity with scan results (VirusDetected={VirusDetected})", upload.VirusDetected);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Saved scan results to DB for UploadId={UploadId}", upload.UploadId);

            // 6. Move S3 object to scanned folder
            var sourceKey = upload.ObjectFileKey;
            var destinationKey = $"uploads/scanned/{upload.User.Sub}/{upload.FileId}";
            _logger.LogInformation("Moving S3 object from {SourceKey} to {DestinationKey} in bucket {Bucket}", sourceKey, destinationKey, bucketName);

            try
            {
                var copyRequest = new CopyObjectRequest
                {
                    SourceBucket = bucketName,
                    SourceKey = sourceKey,
                    DestinationBucket = bucketName,
                    DestinationKey = destinationKey
                };

                _logger.LogDebug("Starting S3 copy: {@CopyRequest}", copyRequest);
                var copyResp = await _s3Client.CopyObjectAsync(copyRequest, ct);
                _logger.LogInformation("S3 copy completed. HTTP status code: {StatusCode}", copyResp.HttpStatusCode);

                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = sourceKey
                };

                _logger.LogDebug("Starting S3 delete: {@DeleteRequest}", deleteRequest);
                var deleteResp = await _s3Client.DeleteObjectAsync(deleteRequest, ct);
                _logger.LogInformation("S3 delete completed. HTTP status code: {StatusCode}", deleteResp.HttpStatusCode);
            }
            catch (Exception s3ex)
            {
                _logger.LogError(s3ex, "Failed to move S3 object {Source} -> {Destination}", upload.ObjectFileKey, destinationKey);
                throw;
            }

            upload.ObjectFileKey = destinationKey;
            job.Status = JobStatus.Completed;
            job.UpdatedAt = DateTime.UtcNow;

            _logger.LogDebug("Marking job {JobId} completed and saving DB", job.Id);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Job {JobId} completed successfully", job.Id);
        }
        finally
        {
            _logger.LogDebug("Cleaning up local files. localPath={LocalPath} extractDir={ExtractDir}", localPath, extractDir);
            TryDelete(localPath);
            TryDeleteDirectory(extractDir);
        }
    }


    private async Task<List<string>> ExtractAndValidateZipAsync(string zipPath, string extractDir, CancellationToken ct)
    {
        var extractedFiles = new List<string>();

        _logger.LogDebug("Opening ZIP file {ZipPath}", zipPath);
        using var zip = await ZipFile.OpenReadAsync(zipPath, ct);

        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("Inspecting ZIP entry: {EntryName} (Size={Size})", entry.FullName, entry.Length);

            // Reject path traversal
            if (entry.FullName.Contains(".."))
            {
                _logger.LogWarning("ZIP entry {EntryName} contains path traversal; rejecting", entry.FullName);
                throw new InvalidOperationException("ZIP contains illegal path traversal");
            }

            // Reject hidden/system files
            if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.StartsWith(".", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ZIP entry {EntryName} is hidden/system; rejecting", entry.FullName);
                throw new InvalidOperationException("ZIP contains hidden or system files");
            }

            // Reject nested ZIPs
            if (entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ZIP entry {EntryName} is a nested ZIP; rejecting", entry.FullName);
                throw new InvalidOperationException("Nested ZIPs are not allowed");
            }

            // ⭐ Skip directory entries (critical fix)
            if (entry.FullName.EndsWith("/"))
            {
                _logger.LogDebug("Skipping directory entry {EntryName}", entry.FullName);
                continue;
            }

            // Build destination path
            var destinationPath = Path.Combine(extractDir, entry.FullName);

            // Ensure parent directory exists
            var parentDir = Path.GetDirectoryName(destinationPath)!;
            Directory.CreateDirectory(parentDir);

            _logger.LogDebug("Extracting entry {EntryName} to {DestinationPath}", entry.FullName, destinationPath);

            // Extract file
            await entry.ExtractToFileAsync(destinationPath, overwrite: true, ct);

            extractedFiles.Add(destinationPath);
            _logger.LogInformation(
                "Extracted {EntryName} -> {DestinationPath} (currentCount={Count})",
                entry.FullName, destinationPath, extractedFiles.Count);
        }

        return extractedFiles;
    }


    private void ValidateDicomFiles(List<string> files)
    {
        _logger.LogDebug("Validating {Count} files as DICOM", files.Count);

        foreach (var file in files)
        {
            _logger.LogDebug("Validating DICOM header for file {File}", file);

            var name = Path.GetFileName(file);

            // Skip directories (should never be in the list, but safe)
            if (Directory.Exists(file))
                continue;

            // Open file
            using var fs = File.OpenRead(file);

            if (fs.Length < 132)
            {
                _logger.LogWarning("File {File} is too small to be DICOM (length={Len})", file, fs.Length);
                throw new InvalidOperationException($"Invalid DICOM file: {file}");
            }

            // Read magic at offset 128
            fs.Seek(128, SeekOrigin.Begin);
            var buffer = new byte[4];
            fs.ReadExactly(buffer, 0, 4);

            var magic = Encoding.ASCII.GetString(buffer);
            _logger.LogDebug("DICOM header read for {File}: {Magic}", file, magic);

            // Accept files with DICM magic
            if (magic == "DICM")
            {
                _logger.LogInformation("DICOM validation succeeded for {File}", file);
                continue;
            }

            // Some valid DICOM files do NOT contain the DICM preamble
            // (allowed by the DICOM standard)
            _logger.LogWarning("File {File} missing DICM magic; may be implicit VR DICOM", file);

            // You can choose to accept or reject these.
            // Most systems accept them.
            continue;
        }
    }



    private async Task DownloadFile(
           IAmazonS3 s3,
           string bucket,
           string key,
           string destinationPath,
           CancellationToken ct)
    {
        _logger.LogDebug("Preparing GetObjectRequest for {Bucket}/{Key}", bucket, key);
        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };

        using var response = await s3.GetObjectAsync(request, ct);
        _logger.LogDebug("S3 GetObject completed. Response ContentLength={ContentLength}", response.ContentLength);

        await using var responseStream = response.ResponseStream;
        await using var fileStream = File.Create(destinationPath);

        _logger.LogDebug("Copying response stream to {DestinationPath}", destinationPath);
        await responseStream.CopyToAsync(fileStream, 81920, ct);
        _logger.LogInformation("Download complete to {DestinationPath}", destinationPath);
    }


    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete directory {Path}", path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file {Path}", path);
        }
    }

    private static async Task<Job?> TryDequeueJobAsync(AppDbContext db, string workerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var job = await db.Jobs
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Jobs""
                WHERE ""Status"" = {JobStatus.Pending}
                  AND ""Type"" = 'virus-scan'
                ORDER BY ""Id""
                FOR UPDATE SKIP LOCKED
                LIMIT 1")
            .FirstOrDefaultAsync(ct);

        if (job == null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        job.Status = JobStatus.Processing;
        job.LockedAt = DateTime.UtcNow;
        job.LockedBy = workerId;
        job.Attempts += 1;
        job.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return job;
    }
}