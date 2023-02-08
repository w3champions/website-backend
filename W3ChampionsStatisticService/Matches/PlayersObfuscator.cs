using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Matches
{
    public static class PlayersObfuscator
    {
        public static void ObfuscatePlayersForFFA(params OnGoingMatchup[] matches)
        {
            var ffaGameModes = new List<GameMode>() { GameMode.FFA, GameMode.GM_SC_FFA_4 };

            foreach (var ffaMatch in matches.
                Where(x => x != null && ffaGameModes.Contains(x.GameMode)))
            {
                foreach (var team in ffaMatch.Teams)
                {
                    foreach (var player in team.Players)
                    {
                        player.BattleTag = "*";
                        player.Name = "*";
                        player.CurrentMmr = 0;
                        player.OldMmr = 0;
                        player.CountryCode = null;
                        player.Location = null;
                        player.Twitch = null;
                    }
                }
            }
        }
    }
}
