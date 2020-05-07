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

            var raceStats = player.data.stats;

            var gamesToPortWins = new List<RaceAndWinDto>();
            gamesToPortWins.Add(new RaceAndWinDto(Race.HU, raceStats.human.wins - myPlayer.GetWinsPerRace(Race.HU), true));
            gamesToPortWins.Add(new RaceAndWinDto(Race.OC, raceStats.orc.wins - myPlayer.GetWinsPerRace(Race.OC), true));
            gamesToPortWins.Add(new RaceAndWinDto(Race.NE, raceStats.night_elf.wins - myPlayer.GetWinsPerRace(Race.NE), true));
            gamesToPortWins.Add(new RaceAndWinDto(Race.UD, raceStats.undead.wins - myPlayer.GetWinsPerRace(Race.UD), true));
            gamesToPortWins.Add(new RaceAndWinDto(Race.RnD, raceStats.random.wins - myPlayer.GetWinsPerRace(Race.RnD), true));

            var gamesToPortLosses = new List<RaceAndWinDto>();
            gamesToPortLosses.Add(new RaceAndWinDto(Race.HU, raceStats.human.losses - myPlayer.GetLossPerRace(Race.HU), false));
            gamesToPortLosses.Add(new RaceAndWinDto(Race.OC, raceStats.orc.losses - myPlayer.GetLossPerRace(Race.OC), false));
            gamesToPortLosses.Add(new RaceAndWinDto(Race.NE, raceStats.night_elf.losses - myPlayer.GetLossPerRace(Race.NE), false));
            gamesToPortLosses.Add(new RaceAndWinDto(Race.UD, raceStats.undead.losses - myPlayer.GetLossPerRace(Race.UD), false));
            gamesToPortLosses.Add(new RaceAndWinDto(Race.RnD, raceStats.random.losses - myPlayer.GetLossPerRace(Race.RnD), false));

            var relevantWins = gamesToPortWins.Where(g => g.Count > 0).ToList();
            var relevantLosses = gamesToPortLosses.Where(g => g.Count > 0).ToList();

            player.data.ladder.TryGetValue("10", out var gatewayStatsUs);
            player.data.ladder.TryGetValue("20", out var gatewayStatsEu);
            player.data.ladder.TryGetValue("30", out var gatewayStatsAs);

            var matchFinishedEvents = new List<MatchFinishedEvent>();

            var winsOnUs = PopRange(relevantWins, gatewayStatsUs?.solo?.wins - myPlayer.GateWayStats[0].GameModeStats[0].Wins).ToList();
            var lossesOnUs = PopRange(relevantLosses, gatewayStatsUs?.solo?.losses - myPlayer.GateWayStats[0].GameModeStats[0].Losses).ToList();
            var winsOnEU = PopRange(relevantWins, gatewayStatsEu?.solo?.wins - myPlayer.GateWayStats[1].GameModeStats[0].Wins).ToList();
            var lossesOnEU = PopRange(relevantLosses, gatewayStatsEu?.solo?.losses - myPlayer.GateWayStats[1].GameModeStats[0].Losses).ToList();
            var winsOnAs = PopRange(relevantWins, gatewayStatsAs?.solo?.wins - myPlayer.GateWayStats[2].GameModeStats[0].Wins).ToList();
            var lossesOnAs = PopRange(relevantLosses, gatewayStatsAs?.solo?.losses - myPlayer.GateWayStats[2].GameModeStats[0].Losses).ToList();

            matchFinishedEvents.AddRange(CreatEvents(myPlayer, ref increment, GateWay.Usa, winsOnUs));
            matchFinishedEvents.AddRange(CreatEvents(myPlayer, ref increment, GateWay.Usa,lossesOnUs));
            matchFinishedEvents.AddRange(CreatEvents(myPlayer, ref increment, GateWay.Europe,winsOnEU));
            matchFinishedEvents.AddRange(CreatEvents(myPlayer, ref increment, GateWay.Europe,lossesOnEU));
            matchFinishedEvents.AddRange(CreatEvents(myPlayer, ref increment, GateWay.Asia,winsOnAs));
            matchFinishedEvents.AddRange(CreatEvents(myPlayer, ref increment, GateWay.Asia,lossesOnAs));

            return matchFinishedEvents;
        }

        private IEnumerable<RaceAndWinDto> PopRange(List<RaceAndWinDto> gamesToPortWins, int? soloWins)
        {
            while (soloWins > 0)
            {
                var raceAndWinDto = gamesToPortWins.FirstOrDefault();
                if (raceAndWinDto == null) break;
                gamesToPortWins.Remove(raceAndWinDto);
                soloWins--;
                yield return raceAndWinDto;
            }
        }

        private IEnumerable<MatchFinishedEvent> CreatEvents(PlayerProfile myPlayer,
            ref int increment,
            GateWay gateWay,
            IEnumerable<RaceAndWinDto> winsOnUs)
        {
            var matchFinishedEvents = new List<MatchFinishedEvent>();
            foreach (var wins in winsOnUs)
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