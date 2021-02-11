using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchReadModelHandler : IReadModelHandler
    {
        private readonly IMatchRepository _matchRepository;
        private readonly IPersonalSettingsRepository _personalSettingsRepository;

        public MatchReadModelHandler(
            IMatchRepository matchRepository,
            IPersonalSettingsRepository personalSettingsRepository
            )
        {
            _matchRepository = matchRepository;
            _personalSettingsRepository = personalSettingsRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.WasFakeEvent) return;
            var count = await _matchRepository.Count();
            var matchup = Matchup.Create(nextEvent);

            // Use the player's personal settings to set their location info
            foreach (var team in matchup.Teams)
            {
                foreach (var player in team.Players)
                {
                    var personalSettings = await _personalSettingsRepository.Load(player.BattleTag);
                    if (personalSettings != null)
                    {
                        player.CountryCode = personalSettings.CountryCode;
                        player.Location = personalSettings.Location;
                        player.Country = personalSettings.Country;
                    }
                }
            }

            matchup.Number = count + 1;

            await _matchRepository.Insert(matchup);
            await _matchRepository.DeleteOnGoingMatch(matchup.MatchId);
        }
    }
}
