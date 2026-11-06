using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace W3ChampionsStatisticService.Ports;

public interface IHotkeySetupRepository
{
    /// <summary>Returns null when the player has no stored setup.</summary>
    Task<HotkeySetup> Load(string battleTag);

    /// <summary>Inserts the setup; false when one already exists for the battleTag.</summary>
    Task<bool> TryCreate(HotkeySetup setup);

    /// <summary>Replaces the stored setup only if its revision equals <paramref name="expectedRevision"/>.</summary>
    Task<bool> TryReplace(HotkeySetup setup, long expectedRevision);
}
