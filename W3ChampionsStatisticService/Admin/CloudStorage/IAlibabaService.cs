using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.Admin.CloudStorage;

namespace W3ChampionsStatisticService.Admin.CloudStorage.Alibaba
{
    public interface IAlibabaService
    {
        List<CloudFile> ListFiles();
        void UploadFile(UploadFileRequest req);
        Task<byte[]> DownloadFile(string fileName);
        void DeleteFile(string fileName);
    }
}
