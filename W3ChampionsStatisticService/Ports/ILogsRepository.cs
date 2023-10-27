using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports;

public interface ILogsRepository
{
    Task<List<string>> GetLogfileNames();
    Task<List<string>> GetLogContent(string logfileName);
    Task<Stream> DownloadLog(string logfileName);
}
