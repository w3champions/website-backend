using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.ChatService;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Clans;

/// <summary>
/// Fires a flair change-ping after a successful clan-membership write. Clan tag is part of a player's
/// chat flair, so joining, leaving, being kicked, or having the clan deleted under them all need to
/// reach chat-service.
/// <para>
/// Only the two MEMBERSHIP-persisting methods notify. <c>UpsertClan</c> and <c>DeleteClan</c> carry no
/// battleTags, and every path that calls them also persists the affected memberships through
/// <see cref="UpsertMemberShip"/> or <see cref="SaveMemberShips"/> — including the bulk teardown in
/// <c>ClanCommandHandler.DeleteClan</c>, which calls <see cref="SaveMemberShips"/> twice (former
/// members, then revoked invitees). So the bulk path is covered with no special-casing.
/// </para>
/// </summary>
public class FlairNotifyingClanRepository(
    IClanRepository inner,
    IFlairChangeNotifier notifier) : IClanRepository
{
    public Task TryInsertClan(Clan clan) => inner.TryInsertClan(clan);
    public Task<Clan> LoadClan(string clanId) => inner.LoadClan(clanId);
    public Task UpsertClan(Clan clan) => inner.UpsertClan(clan);
    public Task<ClanMembership> LoadMemberShip(string battleTag) => inner.LoadMemberShip(battleTag);
    public Task DeleteClan(string clanId) => inner.DeleteClan(clanId);
    public Task<List<ClanMembership>> LoadMemberShips(List<string> clanMembers) => inner.LoadMemberShips(clanMembers);
    public Task<List<ClanMembership>> LoadMemberShipsSince(DateTimeOffset from) => inner.LoadMemberShipsSince(from);

    public async Task UpsertMemberShip(ClanMembership clanMemberShip)
    {
        await inner.UpsertMemberShip(clanMemberShip);
        Notify(new[] { clanMemberShip?.BattleTag });
    }

    public async Task SaveMemberShips(List<ClanMembership> clanMembers)
    {
        await inner.SaveMemberShips(clanMembers);
        Notify(clanMembers?.Select(m => m?.BattleTag).ToList());
    }

    private void Notify(IReadOnlyCollection<string> battleTags)
    {
        if (battleTags == null || battleTags.Count == 0) return;

        try
        {
            notifier.NotifyChanged(battleTags);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Flair change-ping dispatch failed after a clan-membership write");
        }
    }
}
