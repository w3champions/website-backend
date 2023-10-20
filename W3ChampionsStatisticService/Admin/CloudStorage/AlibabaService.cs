using System.Threading.Tasks;
using System.Collections.Generic;
using Aliyun.OSS;
using System.IO;
using System.Net;
using System;
using System.Linq;
using W3C.Contracts.Admin.Rewards;

namespace W3ChampionsStatisticService.Rewards.CloudStorage
{
    public class AlibabaService : IAlibabaService
    {
        private readonly string alibabaBucketName = Environment.GetEnvironmentVariable("ALIBABA_BUCKET_NAME") ?? "";
        private readonly string alibabaAccessKey = Environment.GetEnvironmentVariable("ALIBABA_ACCESS_KEY") ?? "";
        private readonly string alibabaSecretKey = Environment.GetEnvironmentVariable("ALIBABA_SECRET_KEY") ?? "";
        private readonly string alibabaEndpoint = Environment.GetEnvironmentVariable("ALIBABA_ENDPOINT") ?? "";

        public List<CloudFile> ListFiles()
        {
            var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
            var listObjectsRequest = new ListObjectsRequest(alibabaBucketName);
            // Provide a simple list of objects in the specified bucket. By default, 100 objects are returned.
            var result = client.ListObjects(listObjectsRequest);
            return result.ObjectSummaries.Select(x => new CloudFile {
                Name = x.Key,
                Size = x.Size / 1024, // Size in kilobytes
                LastModified = x.LastModified
            }).ToList();
        }

        public void UploadFile(UploadFileRequest file)
        {
            var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
            byte[] bytes = Convert.FromBase64String(file.Content);
            using var ms = new MemoryStream(bytes);
            var putObjectRequest = new PutObjectRequest(alibabaBucketName, file.Name, ms);
            PutObjectResult response = client.PutObject(putObjectRequest);
        }

        public async Task<byte[]> DownloadFile(string fileName)
        {
            var client = new OssClient(alibabaEndpoint, alibabaAccessKey, alibabaSecretKey);
            MemoryStream ms = null;

            GetObjectRequest getObjectRequest = new GetObjectRequest(alibabaBucketName, fileName);

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
            client.DeleteObject(alibabaBucketName, fileName);
        }
    }
}
