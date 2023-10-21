using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.Admin.CloudStorage;

namespace W3ChampionsStatisticService.Admin.CloudStorage.S3
{
    public interface IS3Service
    {
        Task<List<CloudFile>> ListFiles();
        Task UploadFile(UploadFileRequest req);
        Task<byte[]> DownloadFile(string fileName);
        Task DeleteFile(string fileName);
    }
}
