using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.IO.Compression;

namespace FileUploader.VirusScanner;

internal class ZipExtractor
{
    private readonly ILogger<ZipExtractor> _logger;

    public ZipExtractor(ILogger<ZipExtractor> logger)
    {
        _logger = logger;
    }

    internal async Task<ExtractResult> ExtractAndValidate(string zipPath, string extractDir, CancellationToken ct)
    {
        var acceptedFiles = new List<string>();
        var rejectedFiles = new List<(string FileName, string Reason)>();

        _logger.LogDebug("Opening ZIP file {ZipPath}", zipPath);
        using var zip = await ZipFile.OpenReadAsync(zipPath, ct);

        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("Inspecting ZIP entry: {EntryName} {Size}", entry.FullName, entry.Length);

            // Reject path traversal
            if (entry.FullName.Contains(".."))
            {
                _logger.LogWarning("ZIP entry {EntryName} contains path traversal; rejecting", entry.FullName);
                rejectedFiles.Add((entry.FullName, "Path traversal detected"));
                continue;
            }

            // Reject hidden/system files
            if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.StartsWith(".", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ZIP entry {EntryName} is hidden/system; rejecting", entry.FullName);
                rejectedFiles.Add((entry.FullName, "ZIP contains hidden or system files"));
                continue;
            }

            // Reject nested ZIPs
            if (entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ZIP entry {EntryName} is a nested ZIP; rejecting", entry.FullName);
                rejectedFiles.Add((entry.FullName, "ZIP contains nested ZIPs"));
                continue;
            }

            // Skip directory entries
            if (Path.EndsInDirectorySeparator(entry.FullName))
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

            acceptedFiles.Add(destinationPath);

            _logger.LogInformation(
                "Extracted {EntryName} to {DestinationPath} {FileCount}",
                entry.FullName, destinationPath, acceptedFiles.Count);
        }

        return new ExtractResult(
            Success: rejectedFiles.Any(),
            AcceptedFiles: acceptedFiles.ToImmutableList(),
            RejectedFiles: rejectedFiles.ToImmutableList());
    }
}

internal record ExtractResult(
    bool Success,
    IImmutableList<string> AcceptedFiles,
    IImmutableList<(string FileName, string Reason)> RejectedFiles);