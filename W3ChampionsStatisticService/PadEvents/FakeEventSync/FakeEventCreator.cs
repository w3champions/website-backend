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

            var gatewayStatsWins = gatewayStats.Wins;
            var gatewayStatsLosses = gatewayStats.Losses;

            var matchFinishedEvents = new List<MatchFinishedEvent>();
            var winDiffs = new List<RaceAndWinDto>();
            var lossDiffs = new List<RaceAndWinDto>();

            winDiffs.Add(new RaceAndWinDto(Race.HU, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.HU)));
            lossDiffs.Add(new RaceAndWinDto(Race.HU, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.HU)));

            winDiffs.Add(new RaceAndWinDto(Race.OC, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.OC)));
            lossDiffs.Add(new RaceAndWinDto(Race.OC, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.OC)));

            winDiffs.Add(new RaceAndWinDto(Race.NE, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.NE)));
            lossDiffs.Add(new RaceAndWinDto(Race.NE, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.NE)));

            winDiffs.Add(new RaceAndWinDto(Race.UD, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.UD)));
            lossDiffs.Add(new RaceAndWinDto(Race.UD, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.UD)));

            winDiffs.Add(new RaceAndWinDto(Race.RnD, player.Data.Stats.Human.Wins - myPlayer.GetWinsPerRace(Race.RnD)));
            lossDiffs.Add(new RaceAndWinDto(Race.RnD, player.Data.Stats.Human.Wins - myPlayer.GetLossPerRace(Race.RnD)));

            var remainingLosses = await _tempLossesRepo.LoadLosses(player.Account);
            var remainingWins = await _tempLossesRepo.LoadWins(player.Account);
            var newDiffLosses = MergeDiff(remainingLosses, lossDiffs);
            var newDiffWins = MergeDiff(remainingWins, winDiffs);

            matchFinishedEvents.AddRange(CreateGamesOfDiffs(true, myPlayer, increment, winDiffs, gatewayStatsWins));
            matchFinishedEvents.AddRange(CreateGamesOfDiffs(false, myPlayer, increment + matchFinishedEvents.Count, lossDiffs, gatewayStatsLosses));

            await _tempLossesRepo.SaveWins(player.Account, newDiffWins);
            await _tempLossesRepo.SaveLosses(player.Account, newDiffLosses);

            return matchFinishedEvents;
        }

        private List<RaceAndWinDto> MergeDiff(
            List<RaceAndWinDto> remainingLosses,
            List<RaceAndWinDto> lossDiffs)
        {
            return remainingLosses.Zip(
                lossDiffs,
                (dto, winDto) => new RaceAndWinDto(dto.Race, Math.Abs((long) (dto.Count - winDto.Count)))).ToList();
        }

        private List<MatchFinishedEvent> CreateGamesOfDiffs(
            bool won,
            PlayerProfile myPlayer,
            int increment,
            List<RaceAndWinDto> winDiffs,
            long gatewayStats)
        {
            var gateWay = myPlayer.Id.Split("@")[0];
            var finishedEvents = new List<MatchFinishedEvent>();
            foreach (var winDiff in winDiffs)
            {
                while (winDiff.Count != 0 && gatewayStats != 0)
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
                        Id = new
                            ObjectId
                            (_dateTime,
                                0, 0, increment)
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