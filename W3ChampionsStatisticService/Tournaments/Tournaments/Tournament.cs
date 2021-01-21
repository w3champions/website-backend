using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.Tournaments.Tournaments
{
    public class Tournament
    {
        [JsonIgnore] public ObjectId Id { get; set; }

        [JsonPropertyName("id")] public string ObjectId => Id.ToString();

        public string Name { get; set; }

        public List<TournamentRound> WinnerBracketRounds { get; set; }
        public List<TournamentRound> LoserBracketRounds { get; set; }

        public DateTime CreatedOn { get; set; }
        public DateTime StartsOn { get; set; }

        public string MatcherinoLink { get; set; }

        [BsonIgnore]
        [JsonIgnore]
        public string First
        {
            get
            {
                var final = GetFinal();
                return final.Winner;
            }
        }

        [BsonIgnore]
        [JsonIgnore]
        public string Second
        {
            get
            {
                var final = GetFinal();
                return final.Looser;
            }
        }

        [BsonIgnore]
        [JsonIgnore]
        public string Third
        {
            get
            {
                if (LoserBracketRounds == null) return null;

                var tournamentRounds = LoserBracketRounds.OrderBy(w => w.Round);
                var lowerBracketLooser = tournamentRounds.Last().Matches.Single().Looser;
                return lowerBracketLooser;
            }
        }

        [BsonIgnore]
        [JsonIgnore]
        public string[] ThirdAndForth
        {
            get
            {
                if (LoserBracketRounds != null) return null;

                var tournamentRounds = WinnerBracketRounds.OrderBy(w => w.Round);
                var thirdAndForth = tournamentRounds.SkipLast(1).Last().Matches.Select(m => m.Looser).ToArray();
                return thirdAndForth;
            }
        }

        [BsonIgnore]
        [JsonIgnore]
        public string[] Participants
        {
            get
            {
                var winnerRoundBattleTags = WinnerBracketRounds.SelectMany(w => w.Matches.SelectMany(m => m.Players.Select(p => p.BattleTag))).ToList();
                var loserBracketRounds = LoserBracketRounds ?? new List<TournamentRound>();
                var looserRoundBattleTags = loserBracketRounds
                    .SelectMany(w => w.Matches
                        .SelectMany(m => m.Players
                            .Select(p => p.BattleTag))).ToList();

                winnerRoundBattleTags.AddRange(looserRoundBattleTags);

                return winnerRoundBattleTags.ToArray();
            }
        }

        [BsonIgnore]
        [JsonIgnore]
        public string Forth
        {
            get
            {
                if (LoserBracketRounds == null) return null;

                var tournamentRounds = LoserBracketRounds.OrderBy(w => w.Round);
                var lowerBracketLooser = tournamentRounds.SkipLast(1).Last().Matches.Single().Looser;
                return lowerBracketLooser;
            }
        }

        private TournamentMatch GetFinal()
        {
            var tournamentRounds = WinnerBracketRounds.OrderBy(w => w.Round);
            var final = tournamentRounds.Last().Matches.Single();
            return final;
        }
    }
}
