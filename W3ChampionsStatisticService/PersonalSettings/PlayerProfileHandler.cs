using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PlayerRaceWinModelHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerRaceWinModelHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.match.players)
            {
                var player = await _playerRepository.LoadPlayerRaceWins(playerRaw.battleTag)
                             ?? PlayerRaceWins.Create(playerRaw.battleTag);
                player.RecordWin(
                    playerRaw.race,
                    playerRaw.won);
                await _playerRepository.UpsertPlayerRaceWin(player);
            }
        }
    }

    public class PlayerRaceWins : IIdentifiable
    {

        public static PlayerRaceWins Create(string battleTag)
        {
            return new PlayerRaceWins
            {
                BattleTag = battleTag,
                WinLosses = new List<RaceWinLoss>
                {
                    new RaceWinLoss(Race.HU),
                    new RaceWinLoss(Race.OC),
                    new RaceWinLoss(Race.NE),
                    new RaceWinLoss(Race.UD),
                    new RaceWinLoss(Race.RnD)
                }
            };
        }

        public List<RaceWinLoss> WinLosses { get; set; }

        public string BattleTag { get; set; }

        public void RecordWin(Race race, bool won)
        {
            WinLosses.Single(w => w.Race == race).RecordWin(won);
        }

        public string Id => BattleTag;
    }
}