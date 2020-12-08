using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchQueryHandler
    {
        private readonly IPersonalSettingsRepository _personalSettingsRepository;

        public MatchQueryHandler(IPersonalSettingsRepository personalSettingsRepository)
        {
            _personalSettingsRepository = personalSettingsRepository;
        }
        
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
}