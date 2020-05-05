using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class FakeEventCreator
    {
        private readonly ITempLossesRepo _tempLossesRepo;

        public FakeEventCreator(ITempLossesRepo tempLossesRepo)
        {
            _tempLossesRepo = tempLossesRepo;
        }

        private DateTime _dateTime = DateTime.Now.AddDays(-60);

        public async Task<IEnumerable<MatchFinishedEvent>> CreatFakeEvents(PlayerStatePad player, PlayerProfile myPlayer, int increment)
        {
            _dateTime = _dateTime.AddMinutes(increment);
            var gateWay = myPlayer.Id.Split("@")[1];
            player.Data.Ladder.TryGetValue(gateWay, out var gatewayStats);
            if (gatewayStats == null) return new List<MatchFinishedEvent>();

            var padRaceStats = player.Data.Stats;
            var gatewayStatsWins = gatewayStats.Wins;
            var gatewayStatsLosses = gatewayStats.Losses;

            var matchFinishedEvents = new List<MatchFinishedEvent>();

            var remainingWins = await _tempLossesRepo.LoadWins(player.Account);
            var remainingLosses = await _tempLossesRepo.LoadLosses(player.Account);

            if (remainingWins == null)
            {
                remainingWins = new List<RaceAndWinDto>();
                remainingWins.Add(new RaceAndWinDto(Race.HU, padRaceStats.Human.Wins - myPlayer.GetWinsPerRace(Race.HU)));
                remainingWins.Add(new RaceAndWinDto(Race.OC, padRaceStats.Orc.Wins - myPlayer.GetWinsPerRace(Race.OC)));
                remainingWins.Add(new RaceAndWinDto(Race.NE, padRaceStats.NightElf.Wins - myPlayer.GetWinsPerRace(Race.NE)));
                remainingWins.Add(new RaceAndWinDto(Race.UD, padRaceStats.Undead.Wins - myPlayer.GetWinsPerRace(Race.UD)));
                remainingWins.Add(new RaceAndWinDto(Race.RnD, padRaceStats.Random.Wins - myPlayer.GetWinsPerRace(Race.RnD)));
            }

            if (remainingLosses == null)
            {
                remainingLosses = new List<RaceAndWinDto>();
                remainingLosses.Add(new RaceAndWinDto(Race.HU, padRaceStats.Human.Losses - myPlayer.GetLossPerRace(Race.HU)));
                remainingLosses.Add(new RaceAndWinDto(Race.OC, padRaceStats.Orc.Losses - myPlayer.GetLossPerRace(Race.OC)));
                remainingLosses.Add(new RaceAndWinDto(Race.NE, padRaceStats.NightElf.Losses - myPlayer.GetLossPerRace(Race.NE)));
                remainingLosses.Add(new RaceAndWinDto(Race.UD, padRaceStats.Undead.Losses - myPlayer.GetLossPerRace(Race.UD)));
                remainingLosses.Add(new RaceAndWinDto(Race.RnD, padRaceStats.Random.Losses - myPlayer.GetLossPerRace(Race.RnD)));
            }

            matchFinishedEvents.AddRange(CreateGamesOfDiffs(
                true,
                myPlayer,
                increment,
                remainingWins,
                gatewayStatsWins - myPlayer.TotalWins));
            matchFinishedEvents.AddRange(CreateGamesOfDiffs(
                false,
                myPlayer,
                increment + matchFinishedEvents.Count,
                remainingLosses,
                gatewayStatsLosses - myPlayer.TotalLosses));

            await _tempLossesRepo.SaveWins(player.Account, remainingWins);
            await _tempLossesRepo.SaveLosses(player.Account, remainingLosses);

            return matchFinishedEvents;
        }

        private List<MatchFinishedEvent> CreateGamesOfDiffs(
            bool won,
            PlayerProfile myPlayer,
            int increment,
            List<RaceAndWinDto> winDiffs,
            long gatewayStats)
        {
            var gateWay = myPlayer.Id.Split("@")[1];
            var finishedEvents = new List<MatchFinishedEvent>();
            foreach (var winDiff in winDiffs)
            {
                while (winDiff.Count > 0 && gatewayStats > 0)
                {
                    finishedEvents.Add(new MatchFinishedEvent
                    {
                        match = CreatMatch(
                            won,
                            int.Parse(gateWay),
                            myPlayer.CombinedBattleTag,
                            myPlayer.Id,
                            winDiff.Race),
                        WasFakeEvent = true,
                        Id = new ObjectId(_dateTime, 0, 0, increment)
                    });
                    increment++;
                    winDiff.Count--;
                    gatewayStats--;
                }
            }

            return finishedEvents;
        }

        private Match CreatMatch(bool won, int gateWay, string battleTag, string playerId, Race race)
        {
            return new Match
            {
                id = Guid.NewGuid().ToString(),
                gameMode = GameMode.GM_1v1,
                gateway = gateWay,
                players = new List<PlayerMMrChange>
                {
                    new PlayerMMrChange
                    {
                        battleTag = battleTag,
                        won = won,
                        race = race,
                        id = playerId
                    },
                    new PlayerMMrChange
                    {
                        battleTag = "FakeEnemy#123",
                        won = !won,
                        race = Race.RnD,
                        id = "FakeEnemy#123@10"
                    }

                }

            };
        }
    }
}