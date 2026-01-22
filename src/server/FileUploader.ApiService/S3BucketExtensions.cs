using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;

namespace FileUploader.ApiService;

public static class S3BucketExtensions
{
    extension(IAmazonS3 s3)
    {
        public async Task EnsureBucketExistsWithRetriesAsync(
            string bucketName,
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    try
                    {
                        await s3.HeadBucketAsync(new HeadBucketRequest
                        {
                            BucketName = bucketName
                        }, cancellationToken);
                        return;
                    }
                    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {

                    }

                    await s3.PutBucketAsync(new PutBucketRequest
                    {
                        BucketName = bucketName
                    }, cancellationToken);

                    return;
                }
                catch (AmazonS3Exception)
                {
                    if (attempt == 10)
                        throw;

                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
    }
}
