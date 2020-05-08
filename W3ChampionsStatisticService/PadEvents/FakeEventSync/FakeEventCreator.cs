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
        private DateTime _dateTime = DateTime.Now.AddDays(-60);

        public async Task<List<MatchFinishedEvent>> CreatFakeEvents(
            PlayerStatePad player,
            PlayerProfile myPlayer,
            int increment)
        {
            _dateTime = _dateTime.AddSeconds(-increment);

            //todo
            var maxRace = Race.NE;

            player.data.ladder.TryGetValue("10", out var gatewayStatsUs);
            player.data.ladder.TryGetValue("20", out var gatewayStatsEu);
            player.data.ladder.TryGetValue("30", out var gatewayStatsAs);
            var matchFinishedEvents = new List<MatchFinishedEvent>();


            var winsOnUsToGo = gatewayStatsUs?.solo?.wins ?? 0 - myPlayer.GateWayStats[0].GameModeStats[0].Wins;

            CreateGames(winsOnUsToGo, matchFinishedEvents, ref increment);


            return matchFinishedEvents;
        }

        private List<MatchFinishedEvent> CreateGames(int winsToGo, Race race, ref int increment)
        {
            var matchFinishedEvents = new List<MatchFinishedEvent>();

            while (winsToGo > 0)
            {
                winsToGo--;
                new MatchFinishedEvent
                {
                    match = CreatMatch(wins.Won,
                        gateWay,
                        myPlayer.BattleTag,
                        wins.Race),
                    WasFakeEvent = true,
                    Id = new ObjectId(_dateTime,
                        0,
                        0,
                        increment++)
                });
            }

            return matchFinishedEvents;
        }

        private IEnumerable<MatchFinishedEvent> CreatEvents(
            PlayerProfile myPlayer,
            ref int increment,
            GateWay gateWay,
            IEnumerable<RaceAndWinDto> winsOnUs)
        {
            var matchFinishedEvents = new List<MatchFinishedEvent>();
            whi (var wins in winsOnUs)
            {
                matchFinishedEvents.Add(
                new MatchFinishedEvent
                {
                    match = CreatMatch(wins.Won,
                        gateWay,
                        myPlayer.BattleTag,
                        wins.Race),
                    WasFakeEvent = true,
                    Id = new ObjectId(_dateTime,
                        0,
                        0,
                        increment++)
                });
            }

            return matchFinishedEvents;
        }

        private Match CreatMatch(bool won, GateWay gateWay, string battleTag, Race race)
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