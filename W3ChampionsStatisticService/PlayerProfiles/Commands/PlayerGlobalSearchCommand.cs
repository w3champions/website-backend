using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.Search;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.PlayerProfiles.Commands
{
    public class PlayerGlobalSearchCommand
    {
        public readonly IRankRepository _rankRepository;
        public readonly IPersonalSettingsRepository _personalSettingsRepository;
        private readonly string Search;
        private int Limit;
        private readonly int Offset;
        public List<PlayerGlobalSearch> Results;
        public PlayerGlobalSearchCommand(
            string search,
            int limit,
            int offset,
            IRankRepository rankRepository,
            IPersonalSettingsRepository personalSettingsRepository)
        {
          Search=search;
          Limit=limit;
          Offset=offset;
          _rankRepository=rankRepository;
          _personalSettingsRepository=personalSettingsRepository;
          Results=new List<PlayerGlobalSearch>();
        }

        public async Task execute()
        {
          var players = await _rankRepository.SearchAllPlayersForGlobalSearch(Search);
          if (players.Count < 1)
          {
            return;
          }
          var battleTags = players.Select(p => p.Player.PlayerIds[0].BattleTag).ToArray();
          var personalSettings = await _personalSettingsRepository.LoadMany(battleTags);
          processResults(players, personalSettings);
        }

        private void processResults(List<PlayerInfoForGlobalSearch> players, List<PersonalSetting> personalSettings)
        {
            var results = from p in players
                          join ps in personalSettings
                          on p.Player.PlayerIds[0].BattleTag equals ps.Id
                          select new PlayerGlobalSearch(
                              p.Player.PlayerIds[0].BattleTag,
                              p.Player.PlayerIds[0].Name,
                              ps.ProfilePicture,
                              new[] { p.Player.Season },
                              new List<LatestSeasonLeague>(){
                                  new LatestSeasonLeague{
                                      GameMode=p.Player.GameMode,
                                      League=p.RankInfo.League,
                                      Season=p.RankInfo.Season,
                                      RankNumber=p.RankInfo.RankNumber,
                                  }
                              });
            Results=results
                .GroupBy(pgs => pgs.BattleTag, 
                    (battleTag, pgs) => new PlayerGlobalSearch(
                        pgs.First().BattleTag,
                        pgs.First().Name,
                        pgs.First().Picture,
                        pgs.Select(pgs => pgs.ParticipatedInSeasons[0]).ToArray().Distinct().ToArray(),
                        pgs.Select(pgs => pgs.LatestSeasonLeague)
                            .ToList()
                            .SelectMany(x => x)
                            .Distinct()
                            .ToList()
                    ))
                    .ToList()
                    .FindAll(pgs => pgs.Name.ToLower().Contains(Search.ToLower()));
            if (Offset*Limit >= Results.Count)
            {
                Results = new List<PlayerGlobalSearch>();
                return;
            }
            if ((Limit*Offset + Limit) >= Results.Count)
            {
                Results = Results.GetRange(Offset*Limit, Results.Count - Offset * Limit);
                return;
            }
            Results = Results.GetRange(Offset*Limit, Limit);
        }

    }
}