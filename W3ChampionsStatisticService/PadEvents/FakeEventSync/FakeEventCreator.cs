using System;
using System.Collections.Generic;
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

        public async Task<List<MatchFinishedEvent>> CreatFakeEvents(
            PlayerStatePad player,
            PlayerProfile myPlayer,
            int increment)
        {
            _dateTime = _dateTime.AddSeconds(-increment);
            var gateWay = myPlayer.Id.Split("@")[1];
            player.data.ladder.TryGetValue(gateWay, out var gatewayStats);
            if (gatewayStats == null) return new List<MatchFinishedEvent>();

            var padRaceStats = player.data.stats;
            var gatewayStatsWins = gatewayStats.solo.wins;
            var gatewayStatsLosses = gatewayStats.solo.losses;

            var matchFinishedEvents = new List<MatchFinishedEvent>();

            var remainingWins = await _tempLossesRepo.LoadWins(player.account);
            var remainingLosses = await _tempLossesRepo.LoadLosses(player.account);

            if (remainingWins == null)
            {
                remainingWins = new List<RaceAndWinDto>();
                remainingWins.Add(new RaceAndWinDto(Race.HU, padRaceStats.human.wins - myPlayer.GetWinsPerRace(Race.HU)));
                remainingWins.Add(new RaceAndWinDto(Race.OC, padRaceStats.orc.wins - myPlayer.GetWinsPerRace(Race.OC)));
                remainingWins.Add(new RaceAndWinDto(Race.NE, padRaceStats.night_elf.wins - myPlayer.GetWinsPerRace(Race.NE)));
                remainingWins.Add(new RaceAndWinDto(Race.UD, padRaceStats.undead.wins - myPlayer.GetWinsPerRace(Race.UD)));
                remainingWins.Add(new RaceAndWinDto(Race.RnD, padRaceStats.random.wins - myPlayer.GetWinsPerRace(Race.RnD)));
            }

            if (remainingLosses == null)
            {
                remainingLosses = new List<RaceAndWinDto>();
                remainingLosses.Add(new RaceAndWinDto(Race.HU, padRaceStats.human.losses - myPlayer.GetLossPerRace(Race.HU)));
                remainingLosses.Add(new RaceAndWinDto(Race.OC, padRaceStats.orc.losses - myPlayer.GetLossPerRace(Race.OC)));
                remainingLosses.Add(new RaceAndWinDto(Race.NE, padRaceStats.night_elf.losses - myPlayer.GetLossPerRace(Race.NE)));
                remainingLosses.Add(new RaceAndWinDto(Race.UD, padRaceStats.undead.losses - myPlayer.GetLossPerRace(Race.UD)));
                remainingLosses.Add(new RaceAndWinDto(Race.RnD, padRaceStats.random.losses - myPlayer.GetLossPerRace(Race.RnD)));
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

            await _tempLossesRepo.SaveWins(player.account, remainingWins);
            await _tempLossesRepo.SaveLosses(player.account, remainingLosses);

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
                            myPlayer.BattleTag,
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
                        race = race
                    },
                    new PlayerMMrChange
                    {
                        battleTag = "FakeEnemy#123",
                        won = !won,
                        race = Race.RnD
                    }
                }
            };
        }
    }
}