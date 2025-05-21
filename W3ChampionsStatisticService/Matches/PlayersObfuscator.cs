using System.Linq;
using W3C.Domain.GameModes;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Matches;

[Trace]
public static class PlayersObfuscator
{
    public static void ObfuscatePlayersForFFA(params OnGoingMatchup[] matches)
    {
        foreach (var ffaMatch in matches.
            Where(x => x != null && GameModesHelper.IsFfaGameMode(x.GameMode)))
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
