using System.Text;
using tusdotnet.Constants;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace FileUploader.ApiService;

public class FileValidator
{
    private const int MaxFileNameLength = 256;
    private const int MaxBytes = 100 * 1024 * 1024 * 20; // 2 GB
    private static readonly string[] s_allowedExtensions = [".jpg", ".jpeg", ".png", ".pdf", ".zip", ".mp4", ".bmp"];
    private static readonly string[] s_allowedMimeTypes = ["application/x-zip-compressed"];

    public Task BeforeCreate(BeforeCreateContext ctx)
    {
        // You can inspect and modify the create request here.
        // For example, you could reject the upload based on metadata

        var metaData = Metadata.Parse(ctx.HttpContext.Request.Headers[HeaderConstants.UploadMetadata]);

        foreach (var item in metaData)
        {
            var v = item.Value.GetString(Encoding.UTF8);
            Console.WriteLine($"{item.Key}:{v}");
        }

        var fileName = metaData["filename"].GetString(Encoding.UTF8);

        if (fileName.Length > MaxFileNameLength)
        {
            ctx.FailRequest(
                System.Net.HttpStatusCode.BadRequest,
                $"Filename cannot exceed {256} characters");

            return Task.CompletedTask;
        }

        var ext = Path.GetExtension(fileName);

        if (!s_allowedExtensions.Contains(ext))
        {
            ctx.FailRequest(
                System.Net.HttpStatusCode.BadRequest,
                $"Invalid file extension. Allowed extensions: {string.Join(", ", s_allowedExtensions)}");

            return Task.CompletedTask;
        }

        var fileType = metaData["filetype"].GetString(Encoding.UTF8);

        if(!s_allowedMimeTypes.Contains(fileType))
        {
            ctx.FailRequest(
                System.Net.HttpStatusCode.BadRequest,
                $"Invalid mime type. Allowed mime types: {string.Join(", ", s_allowedMimeTypes)}");
         
            return Task.CompletedTask;
        }

        if (ctx.HttpContext.Request.Headers.TryGetValue(HeaderConstants.UploadLength, out var lengthValues))
        {
            if (long.TryParse(lengthValues.ToString(), out var length))
            {
                if (length > MaxBytes)
                {
                    ctx.FailRequest(
                        System.Net.HttpStatusCode.RequestEntityTooLarge,
                        $"Maximum allowed upload size is {MaxBytes} bytes.");

                    return Task.CompletedTask;
                }
            }
            else
            {
                ctx.FailRequest(
                    System.Net.HttpStatusCode.BadRequest,
                    $"Invalid Upload-Length header.");

                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
