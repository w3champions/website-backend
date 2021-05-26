using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Achievements.Models;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Achievements {
    public class AchievementRepositoryHandler : IReadModelHandler  {
        private readonly IPlayerStatsRepository _playerRepository;
        private readonly IAchievementRepository _achievementRepository;

        public AchievementRepositoryHandler(
            IPlayerStatsRepository playerRepository,
            IAchievementRepository achievementRepository) {
            _playerRepository = playerRepository;
            _achievementRepository = achievementRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent) {
            if (nextEvent == null || nextEvent.match == null || nextEvent.result == null) {
                return;
            }
        } 
    }
}