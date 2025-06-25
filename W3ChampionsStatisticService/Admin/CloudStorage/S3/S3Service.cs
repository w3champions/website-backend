using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using W3C.Contracts.Admin.CloudStorage;
using Amazon.S3;
using Amazon.S3.Model;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Admin.CloudStorage.S3;

[Trace]
public class S3Service : IS3Service
{
    private readonly string S3BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") ?? "";
    private readonly string S3AccessKey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY") ?? "";
    private readonly string S3SecretKey = Environment.GetEnvironmentVariable("S3_SECRET_KEY") ?? "";
    private readonly string S3Endpoint = Environment.GetEnvironmentVariable("S3_ENDPOINT") ?? "";
    private readonly string S3Prefix = Environment.GetEnvironmentVariable("S3_PREFIX") ?? "";

    public async Task<List<CloudFile>> ListFiles()
    {
        AmazonS3Config config = new()
        {
            ServiceURL = S3Endpoint
        };
        var client = new AmazonS3Client(S3AccessKey, S3SecretKey, config);
        ListObjectsRequest listObjectsRequest = new()
        {
            BucketName = S3BucketName,
            Prefix = S3Prefix
        };
        ListObjectsResponse listObjectsResponse = await client.ListObjectsAsync(listObjectsRequest);
        return listObjectsResponse.S3Objects.Select(x => new CloudFile
        {
            Name = x.Key.Substring(x.Key.LastIndexOf('/') + 1), // Do not include the prefix
            Size = x.Size / 1024, // Size in kilobytes
            LastModified = x.LastModified,
        }).ToList();
    }

    public async Task UploadFile([NoTrace] UploadFileRequest file)
    {
        AmazonS3Config config = new()
        {
            ServiceURL = S3Endpoint
        };
        var client = new AmazonS3Client(S3AccessKey, S3SecretKey, config);
        byte[] bytes = Convert.FromBase64String(file.Content);
        PutObjectRequest putObjectRequest = new()
        {
            BucketName = S3BucketName,
            Key = $"{S3Prefix}{file.Name}",
            CannedACL = S3CannedACL.PublicRead,
        };
        using var ms = new MemoryStream(bytes);
        putObjectRequest.InputStream = ms;
        PutObjectResponse response = await client.PutObjectAsync(putObjectRequest);
    }

    public async Task<byte[]> DownloadFile(string fileName)
    {
        AmazonS3Config config = new()
        {
            ServiceURL = S3Endpoint
        };
        var client = new AmazonS3Client(S3AccessKey, S3SecretKey, config);
        MemoryStream ms = null;

        GetObjectRequest getObjectRequest = new()
        {
            BucketName = S3BucketName,
            Key = $"{S3Prefix}{fileName}"
        };
        using (var response = await client.GetObjectAsync(getObjectRequest))
        {
            if (response.HttpStatusCode == HttpStatusCode.OK)
            {
                using (ms = new MemoryStream())
                {
                    await response.ResponseStream.CopyToAsync(ms);
                }
            }
        }

        if (ms is null || ms.ToArray().Length == 0)
        {
            throw new FileNotFoundException($"The file {fileName} was not found.");
        }
        return ms.ToArray();
    }

    public async Task DeleteFile(string fileName)
    {
        AmazonS3Config config = new()
        {
            ServiceURL = S3Endpoint
        };
        var client = new AmazonS3Client(S3AccessKey, S3SecretKey, config);
        await client.DeleteObjectAsync(S3BucketName, $"{S3Prefix}{fileName}");
    }
}
