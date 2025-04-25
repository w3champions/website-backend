using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using W3C.Contracts.Admin.CloudStorage;
using Aliyun.OSS;

namespace W3ChampionsStatisticService.Admin.CloudStorage.Alibaba;

public class AlibabaService : IAlibabaService
{
    private readonly string alibabaBucketName = Environment.GetEnvironmentVariable("ALIBABA_BUCKET_NAME") ?? "";
    private readonly string alibabaAccessKey = Environment.GetEnvironmentVariable("ALIBABA_ACCESS_KEY") ?? "";
    private readonly string alibabaSecretKey = Environment.GetEnvironmentVariable("ALIBABA_SECRET_KEY") ?? "";
    private readonly string alibabaEndpoint = Environment.GetEnvironmentVariable("ALIBABA_ENDPOINT") ?? "";
    private readonly string alibabaPrefix = Environment.GetEnvironmentVariable("ALIBABA_PREFIX") ?? "";

    public List<CloudFile> ListFiles()
    {
        var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
        var listObjectsRequest = new ListObjectsRequest(alibabaBucketName)
        {
            MaxKeys = 1000, // The default value of MaxKeys is 100. The maximum value is 1000.
            Prefix = alibabaPrefix
        };
        var result = client.ListObjects(listObjectsRequest);
        return result.ObjectSummaries
            .Where(x => x.Size != 0) // Filter out the directory containing the files
            .Select(x => new CloudFile
            {
                Name = x.Key.Substring(x.Key.LastIndexOf('/') + 1), // Do not include the prefix
                Size = x.Size / 1024, // Size in kilobytes
                LastModified = x.LastModified
            }).ToList();
    }

    public void UploadFile(UploadFileRequest file)
    {
        var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
        byte[] bytes = Convert.FromBase64String(file.Content);
        using var ms = new MemoryStream(bytes);
        var putObjectRequest = new PutObjectRequest(alibabaBucketName, $"{alibabaPrefix}{file.Name}", ms);
        PutObjectResult response = client.PutObject(putObjectRequest);
    }

    public async Task<byte[]> DownloadFile(string fileName)
    {
        var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
        MemoryStream ms = null;

        GetObjectRequest getObjectRequest = new(alibabaBucketName, $"{alibabaPrefix}{fileName}");

        using var response = client.GetObject(getObjectRequest);
        if (response.HttpStatusCode == HttpStatusCode.OK)
        {
            using (ms = new MemoryStream())
            {
                await response.ResponseStream.CopyToAsync(ms);
            }
        }

        if (ms is null || ms.ToArray().Length == 0)
        {
            throw new FileNotFoundException($"The file {fileName} was not found.");
        }
        return ms.ToArray();
    }

    public void DeleteFile(string fileName)
    {
        var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
        client.DeleteObject(alibabaBucketName, $"{alibabaPrefix}{fileName}");
    }
}
