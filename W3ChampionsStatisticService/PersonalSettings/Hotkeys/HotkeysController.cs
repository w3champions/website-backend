using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

[ApiController]
[Route("api/personal-settings")]
[Trace]
public class HotkeysController(IHotkeySetupRepository repository, TimeProvider timeProvider, ILogger<HotkeysController> logger) : ControllerBase
{
    private readonly IHotkeySetupRepository _repository = repository;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<HotkeysController> _logger = logger;

    [HttpGet("{battleTag}/hotkeys")]
    [BearerCheckIfBattleTagBelongsToAuth]
    public async Task<IActionResult> Get(string battleTag)
    {
        var setup = await _repository.Load(battleTag);
        if (setup == null)
        {
            return NotFound();
        }

        return Ok(new HotkeySetupDto(setup.Mode, setup.CustomKeys, setup.PreferenceSettings, setup.Revision, setup.UpdatedAt));
    }

    [HttpPut("{battleTag}/hotkeys")]
    [BearerCheckIfBattleTagBelongsToAuth]
    [HotkeysRateLimit]
    [RequestSizeLimit(655360)]
    public async Task<IActionResult> Put(string battleTag, [FromBody] PutHotkeySetupRequest request)
    {
        var rawPreferences = request.Preferences ?? "";
        var filtered = SyncedPreferences.Filter(rawPreferences);

        var validation = HotkeySetupValidator.Validate(request.Mode, request.CustomKeys, rawPreferences, filtered);
        if (!validation.IsValid)
        {
            LogOutcome(battleTag, validation.Reason);
            return BadRequest(new HotkeyErrorResponse(validation.Reason));
        }

        var stored = await _repository.Load(battleTag);
        if (stored != null && IsUnchanged(stored, request, filtered))
        {
            LogOutcome(battleTag, "noop");
            return Ok(new HotkeyRevisionResponse(stored.Revision));
        }

        var expected = request.ExpectedRevision;
        var setup = new HotkeySetup
        {
            Id = battleTag,
            Mode = request.Mode,
            CustomKeys = request.CustomKeys ?? "",
            PreferenceSettings = filtered,
            Revision = (expected ?? 0) + 1,
            UpdatedAt = _timeProvider.GetUtcNow(),
        };

        var written = expected == null
            ? await _repository.TryCreate(setup)
            : await _repository.TryReplace(setup, expected.Value);

        if (written)
        {
            LogOutcome(battleTag, expected == null ? "created" : "replaced");
            return Ok(new HotkeyRevisionResponse(setup.Revision));
        }

        var current = await _repository.Load(battleTag);
        if (current != null && IsUnchanged(current, request, filtered))
        {
            LogOutcome(battleTag, "noop");
            return Ok(new HotkeyRevisionResponse(current.Revision));
        }

        LogOutcome(battleTag, "conflict");
        return Conflict(new HotkeyRevisionResponse(current?.Revision));
    }

    private void LogOutcome(string battleTag, string outcome) =>
        _logger.LogInformation("Hotkey setup upload for {BattleTag}: {Outcome}", battleTag, outcome);

    private static bool IsUnchanged(HotkeySetup stored, PutHotkeySetupRequest request, string filtered) =>
        stored.Mode == request.Mode
        && string.Equals(stored.PreferenceSettings, filtered, StringComparison.Ordinal)
        && CustomKeysNormalizer.AreEquivalent(stored.CustomKeys, request.CustomKeys);
}
