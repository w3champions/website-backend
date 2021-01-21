using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.Tournaments.Tournaments;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class TournamentTests
    {
        [Test]
        public void TournamentMatch_WinnerProp_NoWinnerYet()
        {
            var tournamentMatch = new TournamentMatch
            {
                Players = new List<TournamentPlayer>
                {
                    new()
                    {
                        BattleTag = "peter#123"
                    },
                    new()
                    {
                        BattleTag = "wolf#456"
                    }
                }
            };

            Assert.IsNull(tournamentMatch.Winner);
        }
        [Test]
        public void TournamentMatch_WinnerProp_NoWinnerYetWithNumbers()
        {
            var tournamentMatch = new TournamentMatch
            {
                Players = new List<TournamentPlayer>
                {
                    new()
                    {
                        BattleTag = "peter#123",
                        Score = 0
                    },
                    new()
                    {
                        BattleTag = "wolf#456",
                        Score = 0
                    }
                }
            };

            Assert.IsNull(tournamentMatch.Winner);
        }

        [Test]
        public void TournamentMatch_WinnerProp_Winner()
        {
            var tournamentMatch = new TournamentMatch
            {
                Players = new List<TournamentPlayer>
                {
                    new()
                    {
                        BattleTag = "peter#123",
                        Score = 2
                    },
                    new()
                    {
                        BattleTag = "wolf#456",
                        Score = 1
                    }
                }
            };

            Assert.AreEqual("peter#123", tournamentMatch.Winner);
            Assert.AreEqual("wolf#456", tournamentMatch.Looser);
        }

        [Test]
        public void TournamentMatch_WinnerProp_PlayerForfeit()
        {
            var tournamentMatch = new TournamentMatch
            {
                Players = new List<TournamentPlayer>
                {
                    new()
                    {
                        BattleTag = "peter#123",
                        Score = -1
                    },
                    new()
                    {
                        BattleTag = "wolf#456",
                        Score = 0
                    }
                }
            };

            Assert.AreEqual("wolf#456", tournamentMatch.Winner);
            Assert.AreEqual("peter#123", tournamentMatch.Looser);
        }

        [Test]
        public void TournamentMatch_WinnerProp_GameUndecided()
        {
            var tournamentMatch = new TournamentMatch
            {
                Players = new List<TournamentPlayer>
                {
                    new()
                    {
                        BattleTag = "peter#123",
                        Score = 1
                    },
                    new()
                    {
                        BattleTag = "wolf#456",
                        Score = 1
                    }
                }
            };

            Assert.IsNull(tournamentMatch.Winner);
        }

        [Test]
        public void TournamentMatch_WinnerProp_AdminMadeABooboo()
        {
            var tournamentMatch = new TournamentMatch
            {
                Players = new List<TournamentPlayer>
                {
                    new()
                    {
                        BattleTag = "peter#123",
                        Score = 13
                    },
                    new()
                    {
                        BattleTag = "wolf#456",
                        Score = 1
                    }
                }
            };

            Assert.AreEqual("peter#123", tournamentMatch.Winner);
        }
    }
}