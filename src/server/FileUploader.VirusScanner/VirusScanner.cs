using Amazon.S3;
using Amazon.S3.Model;
using FellowOakDicom;
using FileUploader.Data;
using FileUploader.VirusScanner.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using nClam;
using System;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace FileUploader.VirusScanner;

internal class VirusScanner : BackgroundService
{
    private readonly ILogger<VirusScanner> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly ClamClient _clamClient;
    private readonly string _clamScanDirectory;
    private readonly Guid _workerId = Guid.NewGuid();
    private readonly ZipExtractor _zipExtractor;
    private readonly DicomFileValidator _dicomFileValidator;
    private readonly JobQueue<VirusScanPayload> _jobQueue;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public VirusScanner(
        ILogger<VirusScanner> logger,
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ClamClient clamClient,
        ZipExtractor zipExtractor,
        DicomFileValidator dicomFileValidator,
        JobQueue<VirusScanPayload> jobQueue,
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _logger = logger;
        _s3Client = s3Client;
        _clamClient = clamClient;
        _zipExtractor = zipExtractor;

        _clamScanDirectory = configuration["ClamAv:ScanDirectory"]
            ?? throw new InvalidOperationException("ClamAv:ScanDirectory is missing");

        Directory.CreateDirectory(_clamScanDirectory);

        _logger.LogInformation("VirusScanner constructed. {WorkerId} {ScanDir}", _workerId, _clamScanDirectory);
        _dicomFileValidator = dicomFileValidator;
        _jobQueue = jobQueue;
        _dbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var workerLogScope = _logger.BeginScope(new Dictionary<string, object> { ["WorkerId"] = _workerId });

        _logger.LogInformation("VirusScanner started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var job in _jobQueue.Dequeue(_workerId.ToString(), stoppingToken))
                {
                    using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken);

                    using var jobLogScope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["JobId"] = job.JobId,
                    });
                    _logger.LogInformation("Dequeued job");
                    await ProcessJobSafe(db, job, stoppingToken);
                }
            }
            catch (OperationCanceledException e)
            {
                _logger.LogInformation(e, "Operation cancelled, exiting loop");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in worker loop");
            }
            finally
            {
                _logger.LogDebug("Worker loop iteration complete, sleeping 1s");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("VirusScanner stopping");
    }

    private async Task ProcessJobSafe(AppDbContext db, Job<VirusScanPayload> job, CancellationToken ct)
    {
        try
        {
            await ProcessJob(db, job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job");

            await _jobQueue.FailJob(job, ct);
        }
    }

    private async Task ProcessJob(AppDbContext db, Job<VirusScanPayload> job, CancellationToken ct)
    {
        _logger.LogInformation("Processing job");

        _logger.LogDebug("{Job}", job.ToString());

        var upload = await db.Uploads
            .Include(u => u.User)
            .Include(u => u.UploadValidationErrors)
            .Include(u => u.UploadVirusScanResult)
            .SingleOrDefaultAsync(u => u.UploadId == job.Payload.UploadId, ct);

        if (upload is null)
        {
            _logger.LogWarning("Upload {UploadId} not found for job", job.Payload.UploadId);
            throw new InvalidOperationException($"Upload {job.Payload.UploadId} not found");
        }

        _logger.LogInformation("Found upload {UploadId} {FileId}, {OriginalName}",
            upload.UploadId,
            upload.FileId,
            upload.OrignalFileName);

        var bucketName = "bucket";

        // Local file path (ZIP or non-ZIP)
        var localPath = Path.Combine(_clamScanDirectory, upload.FileId);
        _logger.LogDebug("Local path for download: {LocalPath}", localPath);

        // Temp extraction directory (only used for ZIPs)
        var extractDir = Path.Combine(_clamScanDirectory, $"{upload.FileId}_extract");
        _logger.LogDebug("Extract directory (if needed): {ExtractDir}", extractDir);

        try
        {
            _logger.LogInformation("Downloading S3 object {Bucket}/{Key} to {LocalPath}",
                bucketName,
                upload.TempObjectKey,
                localPath);

            await DownloadFile(_s3Client, bucketName, upload.TempObjectKey, localPath, ct);

            _logger.LogInformation("Downloaded S3 object to {LocalPath}", localPath);

            FileInfo li = new FileInfo(localPath);
            _logger.LogDebug("Downloaded file size: {SizeBytes} bytes", li.Exists ? li.Length : -1);

            bool isZip = upload.OrignalFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("Is zip: {IsZip} for file {FileName}", isZip, upload.OrignalFileName);

            if (isZip && !upload.UploadValidationErrors.Any())
            {
                // ZIP/DICOM-specific scanning flow
                _logger.LogInformation("ZIP detected. Creating extract directory {ExtractDir}", extractDir);
                Directory.CreateDirectory(extractDir);

                _logger.LogDebug("Extracting ZIP {LocalPath}", localPath);

                var extractResult = await _zipExtractor.ExtractAndValidate(localPath, extractDir, ct);

                _logger.LogInformation("Extracted {Count} files from ZIP {LocalPath}",
                    extractResult.AcceptedFiles.Count + extractResult.RejectedFiles.Count,
                    localPath);

                if (!extractResult.Success)
                {
                    _logger.LogWarning("Zip exctration validation failed for ZIP {LocalPath}", localPath);
                    upload.UploadValidationErrors.Add(new UploadValidationError
                    {
                        ValidationType = "ZIP Extraction",
                        ValidationErrorMessage = $"Rejected {extractResult.RejectedFiles.Count} files during ZIP extraction: " +
                            string.Join("; ", extractResult.RejectedFiles.Select(f => $"{f.FileName} ({f.Reason})"))
                    });
                    await db.SaveChangesAsync(ct);
                    await _jobQueue.CompleteJob(job, ct);
                    return;
                }

                _logger.LogDebug("Validating {Count} extracted files as DICOM", extractResult.AcceptedFiles.Count);

                var dicomValidationResult = await _dicomFileValidator.ValidateDicomFiles(extractResult.AcceptedFiles, ct);

                if (!dicomValidationResult.Success)
                {
                    _logger.LogWarning("DICOM validation failed for ZIP {LocalPath}", localPath);
                    upload.UploadValidationErrors.Add(new UploadValidationError
                    {
                        ValidationType = "DICOM",
                        ValidationErrorMessage = dicomValidationResult.RejectedFiles.Count > 0
                            ? $"Rejected {dicomValidationResult.RejectedFiles.Count} files during DICOM validation: " +
                                string.Join("; ", dicomValidationResult.RejectedFiles.Select(f => $"{f.File} ({f.Reason})"))
                            : "Unknown DICOM validation failure"
                    });
                    await db.SaveChangesAsync(ct);
                    await _jobQueue.CompleteJob(job, ct);
                    return;
                }

                _logger.LogInformation("DICOM validation passed for {Count} files", dicomValidationResult.AcceptedFiles.Count);
            }

            if (upload.UploadVirusScanResult is null)
            {
                _logger.LogInformation("Sending file {FileId} to ClamAV for scanning (non-zip)", upload.FileId);
                var scanResult = await _clamClient.ScanFileOnServerMultithreadedAsync($"/scan/{upload.FileId}", ct);
                _logger.LogInformation("ClamAV scan completed for {FileId} with {Result}", upload.FileId, scanResult.Result);
                _logger.LogDebug("ClamAV raw result length: {Len}", scanResult.RawResult?.Length ?? 0);

                upload.UploadVirusScanResult = new UploadVirusScanResult
                {
                    Result = (ScanReult)scanResult.Result,
                    RawResult = scanResult.RawResult?.Replace("\0", ""),
                    InfectedFiles = (scanResult.InfectedFiles ?? []).Select(f => new InfectedFile
                    {
                        FileName = f.FileName,
                        VirusName = f.VirusName
                    }).ToList()
                };

                if (scanResult.Result != ClamScanResults.Clean)
                {
                    await _jobQueue.CompleteJob(job, ct);
                    await db.SaveChangesAsync(ct);
                    return;
                }

                await db.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Saved scan results to DB for {UploadId}", upload.UploadId);

            var destinationKey = $"uploads/scanned/{upload.User.Sub}/{upload.FileId}";
            var sourceKey = upload.TempObjectKey;

            if (!await _s3Client.ObjectExists(bucketName, destinationKey, ct))
            {
                // Move S3 object to scanned folder
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

                    upload.PersistedObjectKey = destinationKey;
                }
                catch (AmazonS3Exception s3ex)
                {
                    _logger.LogError(s3ex, "Failed to move S3 object {Source} {Destination}", upload.TempObjectKey, destinationKey);
                    throw;
                }
            }


            if (await _s3Client.ObjectExists(bucketName, sourceKey, ct))
            {
                try
                {
                    var deleteRequest = new DeleteObjectRequest
                    {
                        BucketName = bucketName,
                        Key = sourceKey
                    };

                    _logger.LogDebug("Starting S3 delete: {@DeleteRequest}", deleteRequest);
                    var deleteResp = await _s3Client.DeleteObjectAsync(deleteRequest, ct);
                    _logger.LogInformation("S3 delete completed. HTTP status code: {StatusCode}", deleteResp.HttpStatusCode);
                }
                catch (AmazonS3Exception s3ex)
                {
                    _logger.LogError(s3ex, "Failed to delete S3 object {Source}", sourceKey);

                    throw;
                }
            }

            await _jobQueue.CompleteJob(job, ct);

            _logger.LogDebug("Marking job completed and saving DB");
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Job completed successfully");
        }
        catch (Exception ex)
        {
            await _jobQueue.FailJob(job, ct);
            _logger.LogError(ex, "Error during job processing");
            throw;
        }
        finally
        {
            _logger.LogDebug("Cleaning up local files. {LocalPath} {ExtractDir}", localPath, extractDir);
            TryDelete(localPath);
            TryDeleteDirectory(extractDir);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file {Path}", path);
        }
    }
}
