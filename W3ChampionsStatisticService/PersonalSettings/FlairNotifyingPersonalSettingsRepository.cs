using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.ChatService;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings;

/// <summary>
/// Fires a flair change-ping after a successful settings write, so chat-service can push the new
/// portrait / chat colour / chat icons to anyone currently viewing that player.
/// <para>
/// This hangs off the PERSISTENCE boundary rather than the command handlers deliberately. There are
/// five separate flair-write paths — the portrait handler, the settings controller, both reward
/// modules (which bypass the controller entirely) and the clan handler — and hooking them
/// individually means a sixth added later is silently missed. Every one of them crosses this
/// interface, so covering it here cannot be forgotten.
/// </para>
/// <para>
/// Deliberately over-notifies: it fires on ANY settings save, not only flair-relevant ones.
/// Fingerprinting the flair fields to suppress no-op pings was considered and rejected — it saves a
/// call that only happens for players currently online in chat, at the cost of real machinery and a
/// new way to be subtly wrong. Chat-side coalescing absorbs the volume.
/// </para>
/// </summary>
public class FlairNotifyingPersonalSettingsRepository(
    IPersonalSettingsRepository inner,
    IFlairChangeNotifier notifier) : IPersonalSettingsRepository
{
    public Task<PersonalSetting> Load(string battletag) => inner.Load(battletag);
    public Task<PersonalSetting> LoadOrCreate(string battletag) => inner.LoadOrCreate(battletag);
    public Task<PersonalSetting> Find(string battletag) => inner.Find(battletag);
    public Task<List<PersonalSetting>> LoadSince(DateTimeOffset from) => inner.LoadSince(from);
    public Task<List<PersonalSetting>> LoadMany(string[] battletags) => inner.LoadMany(battletags);
    public Task<List<PersonalSetting>> LoadAll() => inner.LoadAll();

    public async Task Save(PersonalSetting setting)
    {
        await inner.Save(setting);
        Notify(new[] { setting?.Id });
    }

    public async Task SaveMany(List<PersonalSetting> settings)
    {
        await inner.SaveMany(settings);
        Notify(settings?.Select(s => s?.Id).ToList());
    }

    // Notification is strictly after a successful write and can never fail it: if the inner call
    // throws we never get here, and if the notifier throws we swallow it. A player must not lose a
    // settings save because chat-service is unreachable.
    private void Notify(IReadOnlyCollection<string> battleTags)
    {
        if (battleTags == null || battleTags.Count == 0) return;

        try
        {
            notifier.NotifyChanged(battleTags);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Flair change-ping dispatch failed after a personal-settings write");
        }
    }
}
