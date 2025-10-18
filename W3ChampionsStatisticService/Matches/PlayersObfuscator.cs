using System.Linq;
using W3C.Domain.GameModes;

namespace W3ChampionsStatisticService.Matches;

public static class PlayersObfuscator
{
    public const double RankDeviationObfuscationThreshold = 240;
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
                    player.OldMmrQuantile = null;
                    player.CountryCode = null;
                    player.Location = null;
                    player.Twitch = null;
                    player.Ranking = null;
                }
            }

            foreach (var serverInfo in ffaMatch.ServerInfo.PlayerServerInfos)
            {
                serverInfo.CurrentPing = 0;
                serverInfo.AveragePing = 0;
            }
        }
    }

    public static void ObfuscateMmr(params Matchup[] matches)
    {
        if (matches == null) return;

        foreach (var match in matches)
        {
            ObfuscateMmr(match);
        }
    }

    public static void ObfuscateMmr(System.Collections.Generic.List<Matchup> matches)
    {
        if (matches == null) return;

        foreach (var match in matches)
        {
            ObfuscateMmr(match);
        }
    }

    public static void ObfuscateMmr(Matchup match)
    {
        if (match == null) return;

        foreach (var team in match.Teams)
        {
            foreach (var player in team.Players)
            {
                // If the system is still not confident about the MMR, don't expose it.
                if (player.OldRankDeviation != null && player.OldRankDeviation >= RankDeviationObfuscationThreshold)
                {
                    player.CurrentMmr = null;
                    player.OldMmr = null;
                    player.OldMmrQuantile = null;
                }

                // Never expose these values to the outside, they are internal!
                player.OldRankDeviation = null;
            }
        }
    }
}
