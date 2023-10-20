using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.Admin.Rewards;

namespace W3ChampionsStatisticService.Rewards.CloudStorage
{
    public interface IAlibabaService
    {
        List<CloudFile> ListFiles();
        void UploadFile(UploadFileRequest req);
        Task<byte[]> DownloadFile(string fileName);
        void DeleteFile(string fileName);
    }
}
