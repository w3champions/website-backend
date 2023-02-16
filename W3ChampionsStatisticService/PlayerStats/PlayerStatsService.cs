using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.PlayerStats
{
    public class PlayerStatisticsService: IPlayerStatisticsService
    {
        private readonly IPlayerStatsRepository _playerStatsRepository;
        private readonly MatchmakingProvider _matchmakingProvider;
        private readonly ICacheData<SeasonMapInformation> _seasonMapCache;

        public PlayerStatisticsService(IPlayerStatsRepository playerStatsRepository, MatchmakingProvider matchmakingProvider, ICacheData<SeasonMapInformation> seasonMapCache)
        {
            _playerStatsRepository = playerStatsRepository;
            _matchmakingProvider = matchmakingProvider;
            _seasonMapCache = seasonMapCache;
        }

        public async Task<PlayerRaceOnMapVersusRaceRatioView> GetMapAndRaceStatAsync(string battleTag, int season)
        {
            var mapAndRaceStat = await _playerStatsRepository.LoadMapAndRaceStat(battleTag, season);

            var mapInformation = await FetchMapNamesAsync();

            return PlayerRaceOnMapVersusRaceRatioView.Create(mapAndRaceStat, mapInformation.MapNames);
        }

        private async Task<SeasonMapInformation> FetchMapNamesAsync()
        {
            return await _seasonMapCache.GetCachedOrRequestAsync(FetchCurrentSeasonMapsInfoAsync, null);
        }

        private async Task<SeasonMapInformation> FetchCurrentSeasonMapsInfoAsync()
        {
            var seasonMaps = await _matchmakingProvider.GetCurrentSeasonMapsAsync();
            var mapNames = new Dictionary<string, string>();
            foreach (var seasonMap in seasonMaps.Items.SelectMany(x => x.Maps))
            {
                var map = new MapName(seasonMap.GameMap.Path).Name;
                mapNames[map] = seasonMap.GameMap.Name;
            }

            return new SeasonMapInformation(mapNames);
        }
    }

    public class SeasonMapInformation
    {
        public IReadOnlyDictionary<string, string> MapNames { get; }

        public SeasonMapInformation(IReadOnlyDictionary<string, string> mapNames)
        {
            MapNames = mapNames;
        }
    }
}