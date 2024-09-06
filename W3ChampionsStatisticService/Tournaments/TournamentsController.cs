using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.MatchmakingService;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Tournaments;

[ApiController]
[Route("api/tournaments")]
public class TournamentsController(
    MatchmakingServiceClient matchmakingServiceRepository,
    IPersonalSettingsRepository personalSettingsRepository
    ) : ControllerBase
{
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly MatchmakingServiceClient _matchmakingServiceRepository = matchmakingServiceRepository;

    [HttpGet("")]
    public async Task<IActionResult> GetTournaments()
    {
        var tournaments = await _matchmakingServiceRepository.GetTournaments();
        return Ok(tournaments);
    }

    [HttpPost("")]
    [BearerHasPermissionFilter(Permission = EPermission.Tournaments)]
    public async Task<IActionResult> CreateTournament([FromBody] TournamentUpdateBody tournamentData)
    {
        var tournament = await _matchmakingServiceRepository.CreateTournament(tournamentData);
        return Ok(tournament);
    }

    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcomingTournament()
    {
        var tournament = await _matchmakingServiceRepository.GetUpcomingTournament();
        return Ok(tournament);
    }

    [HttpGet("flo-nodes")]
    public async Task<IActionResult> GetEnabledFloNodes()
    {
        var enabledNodes = await _matchmakingServiceRepository.GetEnabledFloNodes();
        return Ok(enabledNodes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTournament(string id)
    {
        var tournament = await _matchmakingServiceRepository.GetTournament(id);
        return Ok(tournament);
    }

    [HttpPatch("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Tournaments)]
    public async Task<IActionResult> UpdateTournament(string id, [FromBody] TournamentUpdateBody updates)
    {
        var tournament = await _matchmakingServiceRepository.UpdateTournament(id, updates);
        return Ok(tournament);
    }

    [HttpPost("{id}/players")]
    [BearerHasPermissionFilter(Permission = EPermission.Tournaments)]
    public async Task<IActionResult> RegisterPlayer(string id, [FromBody] RegisterPlayerBody body)
    {
        var personalSetting = await _personalSettingsRepository.Load(body.BattleTag);
        var tournament = await _matchmakingServiceRepository.RegisterPlayer(id, body.BattleTag, body.Race, personalSetting.CountryCode);
        return Ok(tournament);
    }

    [HttpDelete("{id}/players")]
    [BearerHasPermissionFilter(Permission = EPermission.Tournaments)]
    public async Task<IActionResult> UnregisterPlayer(string id, [FromBody] UnregisterPlayerBody body)
    {
        var tournament = await _matchmakingServiceRepository.UnregisterPlayer(id, body.BattleTag);
        return Ok(tournament);
    }

    public class RegisterPlayerBody
    {
        public string BattleTag { get; set; }
        public Race Race { get; set; }
    }

    public class UnregisterPlayerBody
    {
        public string BattleTag { get; set; }
    }
}
