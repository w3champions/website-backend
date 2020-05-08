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

        public List<MatchFinishedEvent> CreatFakeEvents(
            PlayerStatePad player,
            PlayerProfile myPlayer,
            int increment)
        {
            _dateTime = _dateTime.AddSeconds(-increment);

            var maxRaceCount = myPlayer.RaceStats.Max(r => r.Games);
            var maxRace = myPlayer.RaceStats.First(r => r.Games == maxRaceCount).Race;

            if (player == null) return new List<MatchFinishedEvent>();

            player.data.ladder.TryGetValue("10", out var gatewayStatsUs);
            player.data.ladder.TryGetValue("20", out var gatewayStatsEu);
            player.data.ladder.TryGetValue("30", out var gatewayStatsAs);

            var matchFinishedEvents = new List<MatchFinishedEvent>();

            var winsOnUsToGo = (gatewayStatsUs?.solo?.wins ?? 0) - myPlayer.GateWayStats[0].GameModeStats[0].Wins;
            var lossOnUsToGo = (gatewayStatsUs?.solo?.losses ?? 0) - myPlayer.GateWayStats[0].GameModeStats[0].Losses;
            var winsOnEuToGo = (gatewayStatsEu?.solo?.wins ?? 0) - myPlayer.GateWayStats[1].GameModeStats[0].Wins;
            var lossOnEuToGo = (gatewayStatsEu?.solo?.losses ?? 0) - myPlayer.GateWayStats[1].GameModeStats[0].Losses;
            var winsOnAsToGo = (gatewayStatsAs?.solo?.wins ?? 0) - myPlayer.GateWayStats[2].GameModeStats[0].Wins;
            var lossOnAsToGo = (gatewayStatsAs?.solo?.losses ?? 0) - myPlayer.GateWayStats[2].GameModeStats[0].Losses;

            matchFinishedEvents.AddRange(CreateGames(winsOnUsToGo, true, maxRace, GateWay.Usa, myPlayer.BattleTag, ref increment));
            matchFinishedEvents.AddRange(CreateGames(lossOnUsToGo, false, maxRace, GateWay.Usa, myPlayer.BattleTag, ref increment));
            matchFinishedEvents.AddRange(CreateGames(winsOnEuToGo, true, maxRace, GateWay.Europe, myPlayer.BattleTag, ref increment));
            matchFinishedEvents.AddRange(CreateGames(lossOnEuToGo, false, maxRace, GateWay.Europe, myPlayer.BattleTag, ref increment));
            matchFinishedEvents.AddRange(CreateGames(winsOnAsToGo, true, maxRace, GateWay.Asia, myPlayer.BattleTag, ref increment));
            matchFinishedEvents.AddRange(CreateGames(lossOnAsToGo, false, maxRace, GateWay.Asia, myPlayer.BattleTag, ref increment));

            return matchFinishedEvents;
        }

        private List<MatchFinishedEvent> CreateGames(
            int winsToGo,
            bool won,
            Race race,
            GateWay gateWay,
            string battleTag,
            ref int increment)
        {
            var matchFinishedEvents = new List<MatchFinishedEvent>();

            while (winsToGo > 0)
            {
                winsToGo--;
                matchFinishedEvents.Add(new MatchFinishedEvent
                {
                    match = CreatMatch(won,
                        gateWay,
                        battleTag,
                        race),
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