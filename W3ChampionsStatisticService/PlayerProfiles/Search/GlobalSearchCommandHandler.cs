using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles.Commands;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles.Search
{
  public class GlobalSearchCommandHandler
  {
    private readonly IRankRepository _rankRepository;
    private readonly IPersonalSettingsRepository _personalSettingsRepository;

    public GlobalSearchCommandHandler(IRankRepository rankRepository, IPersonalSettingsRepository personalSettingsRepository)
    {
        _rankRepository=rankRepository;
        _personalSettingsRepository=personalSettingsRepository;
    }

    public async Task<List<PlayerGlobalSearch>> SearchAllPlayersForGlobalSearch(
        string search,
        int limit = 10,
        int offset = 0)
    {
        if (limit > 50) limit = 50;
        var searchCommand = new PlayerGlobalSearchCommand(search, limit, offset, _rankRepository, _personalSettingsRepository);
        await searchCommand.execute();
        return searchCommand.Results;
    }
  }
}