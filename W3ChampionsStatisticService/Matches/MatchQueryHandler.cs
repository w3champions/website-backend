using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public class MatchQueryHandler(IPersonalSettingsRepository personalSettingsRepository)
{
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;

    //public virtual async Task PopulatePlayerInfos(List<OnGoingMatchup> matches)
    public async Task PopulatePlayerInfos(List<OnGoingMatchup> matches)
    {
        var battleTags = matches.SelectMany(match => match.Teams).SelectMany(team => team.Players).Select(player => player.BattleTag);
        var personalSettings = await _personalSettingsRepository.LoadMany(battleTags.ToArray());

        foreach (var match in matches)
        {
            foreach (var team in match.Teams)
            {
                foreach (var player in team.Players)
                {
                    var settings = personalSettings.Find(s => s.Id == player.BattleTag && s.Twitch != null);

                    if (settings != null)
                    {
                        player.Twitch = settings.Twitch;
                    }
                }
            }
        }
    }
}
