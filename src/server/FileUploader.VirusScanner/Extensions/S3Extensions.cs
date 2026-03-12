using Amazon.S3;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace FileUploader.VirusScanner.Extensions;

internal static class S3Extensions
{
    extension(IAmazonS3 s3)
    {
        internal async Task<bool> ObjectExists(
            string bucketName,
            string key,
            CancellationToken ct = default)
        {
            try
            {
                await s3.GetObjectMetadataAsync(bucketName, key, ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}