using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

namespace W3ChampionsStatisticService.Ports;

public interface IPlayerProgressionRepository
{
    Task<PlayerProgression> LoadProgression(string id);
    Task UpsertProgression(PlayerProgression progression);
    Task<List<PlayerProgression>> LoadProgressions(IReadOnlyList<string> ids);
    Task<List<PlayerProgression>> LoadPlayersByProgressionLeague(
        int season, GameMode gameMode, int league, int division, Race? race, int skip, int take);
    Task<List<ProgressionBracketCount>> CountByBracket(int season);
}
