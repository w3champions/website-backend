using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace W3ChampionsStatisticService.Ports;

public interface IHotkeySetupRepository
{
    /// <summary>Returns null when the player has no stored setup.</summary>
    Task<HotkeySetup> Load(string battleTag);

    /// <summary>Inserts the setup; false when one already exists for the battleTag.</summary>
    Task<bool> TryCreate(HotkeySetup setup);

    /// <summary>
    /// Replaces the stored setup only if its stored revision equals <paramref name="expectedRevision"/>.
    /// The caller sets <c>setup.Revision</c> to <c>expectedRevision + 1</c>; the repository stores it as given.
    /// </summary>
    Task<bool> TryReplace(HotkeySetup setup, long expectedRevision);
}
