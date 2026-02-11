using Microsoft.Extensions.Options;
using System.Text;
using tusdotnet.Constants;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace FileUploader.ApiService;

public class UploadOptions
{
    public double MaxFileSize { get; set; }

    public string AllowedExtensions { get; set; } = string.Empty;

    public string AllowedMimeTypes { get; set; } = string.Empty;

    public int MaxFileNameLength { get; set; }

    public string[] AllowedExtensionsArray =>
        AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string[] AllowedMimeTypesArray =>
        AllowedMimeTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}


public class FileValidator
{
    private readonly IOptions<UploadOptions> _options;

    public FileValidator(IOptions<UploadOptions> options)
    {
        _options = options;
    }

    public Task BeforeCreate(BeforeCreateContext ctx)
    {
        var metaData = Metadata.Parse(ctx.HttpContext.Request.Headers[HeaderConstants.UploadMetadata]);

        var fileName = metaData["filename"].GetString(Encoding.UTF8);

        if (fileName.Length > _options.Value.MaxFileNameLength)
        {
            ctx.FailRequest(
                System.Net.HttpStatusCode.BadRequest,
                $"Filename cannot exceed {_options.Value.MaxFileNameLength} characters");

            return Task.CompletedTask;
        }

        var ext = Path.GetExtension(fileName);

        if (!_options.Value.AllowedExtensionsArray.Contains(ext))
        {
            ctx.FailRequest(
                System.Net.HttpStatusCode.BadRequest,
                $"Invalid file extension. Allowed extensions: {string.Join(", ", _options.Value.AllowedExtensionsArray)}");

            return Task.CompletedTask;
        }

        var fileType = metaData["filetype"].GetString(Encoding.UTF8);

        if (!_options.Value.AllowedMimeTypesArray.Contains(fileType))
        {
            ctx.FailRequest(
                System.Net.HttpStatusCode.BadRequest,
                $"Invalid mime type. Allowed mime types: {string.Join(", ", _options.Value.AllowedMimeTypesArray)}");

            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
