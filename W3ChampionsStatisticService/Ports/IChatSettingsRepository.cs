using System.Threading.Tasks;
using W3ChampionsStatisticService.Chats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IChatSettingsRepository
    {
        Task Save(ChatSettings chatSettings);
        Task<ChatSettings> Load(string id);
    }
}