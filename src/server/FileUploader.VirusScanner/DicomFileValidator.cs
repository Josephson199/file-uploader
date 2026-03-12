using FellowOakDicom;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace FileUploader.VirusScanner;

internal class DicomFileValidator
{
    private readonly ILogger<DicomFileValidator> _logger;

    public DicomFileValidator(ILogger<DicomFileValidator> logger)
    {
        _logger = logger;
    }

    internal async Task<DicomFileValidatorResult> ValidateDicomFiles(IImmutableList<string> files, CancellationToken ct)
    {
        _logger.LogDebug("Validating {Count} files as DICOM", files.Count);

        var acceptedFiles = new List<string>();
        var rejectedFiles = new List<(string File, string Reason)>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            // Skip directories
            if (Directory.Exists(file))
                continue;

            _logger.LogDebug("Validating DICOM header for file {File}", file);

            var name = Path.GetFileName(file);

            _logger.LogDebug("Started validating {File} as DICOM", name);

            if (!DicomFile.HasValidHeader(file))
            {
                _logger.LogInformation("DICOM validation failed for {File}. Invalid Header.", file);
                rejectedFiles.Add((file, "Invalid DICOM header"));
            }

            try
            {
                _ = await DicomFile.OpenAsync(file);
            }
            catch (DicomFileException e)
            {
                _logger.LogWarning(e, "DICOM validation failed for {File}", file);
                rejectedFiles.Add((file, $"Failed to open DICOM file with exception: {e.Message}"));
            }

            _logger.LogDebug("Successfully validated {File} as DICOM", name);
        }

        return new DicomFileValidatorResult(
            Success: rejectedFiles.Count == 0,
            AcceptedFiles: acceptedFiles.ToImmutableList(),
            RejectedFiles: rejectedFiles.ToImmutableList());
    }
}

internal record DicomFileValidatorResult(
    bool Success,
    IImmutableList<string> AcceptedFiles,
    IImmutableList<(string File, string Reason)> RejectedFiles);