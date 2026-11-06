using System;

namespace W3ChampionsStatisticService.PersonalSettings.Hotkeys;

/// <summary>PreferenceSettings is deliberately absent: the server always derives it from Preferences.</summary>
public record PutHotkeySetupRequest
{
    public int Mode { get; init; }

    public string CustomKeys { get; init; }

    public string Preferences { get; init; }

    public long? ExpectedRevision { get; init; }
}

public record HotkeySetupDto(int Mode, string CustomKeys, string PreferenceSettings, long Revision, DateTimeOffset UpdatedAt);

public record HotkeyRevisionResponse(long? Revision);

public record HotkeyErrorResponse(string Error);
