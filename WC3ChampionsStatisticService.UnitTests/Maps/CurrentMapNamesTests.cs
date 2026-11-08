using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

namespace WC3ChampionsStatisticService.Tests.Maps;

[TestFixture]
public class CurrentMapNamesTests
{
    private const string EchoIslesFile = "w3c_EchoIsles_v2.2.w3x";
    private const string AutumnLeavesFile = "w3c_AutumnLeaves_v2.0.w3x";
    private const string LegionTdFile = "Legion_TD_11.0_W3C_TeamOZE.w3x";
    // An older version with no id of its own: a seasons 2-8 key kept as Map, with its historical name.
    private const string AutumnLeavesLegacyKey = "autumnleaves";

    private static readonly CurrentMapNames Names = new(new Dictionary<int, string>
    {
        [1051] = "Echo Isles v2",
        [44] = "Autumn Leaves v2",
        [5072] = "Legion TD: X3",
    });

    private static readonly GameMode[] CurrentNameModes =
    [
        GameMode.GM_1v1, GameMode.GM_2v2, GameMode.GM_3v3, GameMode.GM_4v4, GameMode.FFA, GameMode.GM_2v2_AT,
        GameMode.GM_4v4_AT, GameMode.GM_1ON1_TOURNAMENT, GameMode.GM_ATR_1ON1, GameMode.GM_ROC_1ON1, GameMode.GM_PTR_1ON1,
    ];

    [Test]
    [TestCaseSource(nameof(CurrentNameModes))]
    public void Resolve_CurrentNameMode_ShowsCurrentName(GameMode gameMode)
    {
        Assert.That(Names.Resolve(gameMode, 1051, "Echo Isles 2.0"), Is.EqualTo("Echo Isles v2"));
    }

    [Test]
    [TestCase(GameMode.GM_LEGION_1v1_x20)]
    [TestCase(GameMode.GM_DS)]
    [TestCase(GameMode.GM_SC_FFA_4)]
    [TestCase(GameMode.Undefined)]
    public void Resolve_OtherMode_KeepsStoredName(GameMode gameMode)
    {
        Assert.That(Names.Resolve(gameMode, 5072, "Legion TD 11.0"), Is.EqualTo("Legion TD 11.0"));
    }

    [Test]
    public void Resolve_NoIdOrUnknownId_KeepsStoredName()
    {
        Assert.That(Names.Resolve(GameMode.GM_1v1, null, "Autumn Leaves"), Is.EqualTo("Autumn Leaves"));
        Assert.That(Names.Resolve(GameMode.GM_1v1, 6, "Twisted Meadows"), Is.EqualTo("Twisted Meadows"));
        Assert.That(Names.Resolve(GameMode.GM_1v1, null, null), Is.Null);
        Assert.That(CurrentMapNames.Empty.Resolve(GameMode.GM_1v1, 1051, "Echo Isles 2.0"), Is.EqualTo("Echo Isles 2.0"));
    }

    [Test]
    public void Apply_MapsPerSeason_PerMode_AndAllModesBucketFollowsTheModeOfTheId()
    {
        var season = MapsPerSeason.Create(17);
        season.Count(EchoIslesFile, 1051, "Echo Isles 2.0", GameMode.GM_1v1);
        season.Count(AutumnLeavesFile, 44, "Autumn Leaves", GameMode.GM_2v2);
        season.Count(LegionTdFile, 5072, "Legion TD 11.0", GameMode.GM_LEGION_1v1_x20);
        season.Count(AutumnLeavesLegacyKey, null, "Autumn Leaves", GameMode.GM_1v1);

        Names.Apply([season]);

        string NameIn(GameMode mode, string map) =>
            season.MatchesOnMapPerModes.Single(m => m.GameMode == mode).Maps.Single(e => e.Map == map).MapName;
        Assert.That(NameIn(GameMode.GM_1v1, EchoIslesFile), Is.EqualTo("Echo Isles v2"));
        Assert.That(NameIn(GameMode.GM_2v2, AutumnLeavesFile), Is.EqualTo("Autumn Leaves v2"));
        Assert.That(NameIn(GameMode.GM_LEGION_1v1_x20, LegionTdFile), Is.EqualTo("Legion TD 11.0"));
        Assert.That(NameIn(GameMode.GM_1v1, AutumnLeavesLegacyKey), Is.EqualTo("Autumn Leaves"));
        Assert.That(NameIn(GameMode.Undefined, EchoIslesFile), Is.EqualTo("Echo Isles v2"));
        Assert.That(NameIn(GameMode.Undefined, AutumnLeavesFile), Is.EqualTo("Autumn Leaves v2"));
        Assert.That(NameIn(GameMode.Undefined, LegionTdFile), Is.EqualTo("Legion TD 11.0"));
        Assert.That(NameIn(GameMode.Undefined, AutumnLeavesLegacyKey), Is.EqualTo("Autumn Leaves"));
    }

    [Test]
    public void Apply_MapsPerSeason_AllModesBucket_OnlyUsesIdsOfTheSameDocument()
    {
        var melee = MapsPerSeason.Create(17);
        melee.Count(EchoIslesFile, 1051, "Echo Isles 2.0", GameMode.GM_1v1);
        var otherSeason = MapsPerSeason.Create(18);
        otherSeason.MatchesOnMapPerModes.Add(MatchOnMapPerMode.Create(GameMode.Undefined));
        otherSeason.MatchesOnMapPerModes[0].CountMatch(EchoIslesFile, 1051, "Echo Isles 2.0");

        Names.Apply([melee, otherSeason]);

        Assert.That(otherSeason.MatchesOnMapPerModes[0].Maps.Single().MapName, Is.EqualTo("Echo Isles 2.0"));
    }

    [Test]
    public void Apply_HeroStats_AnyEntryWithKnownId_OverallUntouched()
    {
        var heroStats = PlayerHeroStats.Create("Player#123", 17);
        heroStats.HeroStatsItemList.AddWin("archmage", Race.HU, Race.UD, EchoIslesFile, 1051, "Echo Isles 2.0", true);
        heroStats.HeroStatsItemList.AddWin("archmage", Race.HU, Race.UD, AutumnLeavesLegacyKey, null, "Autumn Leaves", true);
        heroStats.HeroStatsItemList.AddWin("archmage", Race.HU, Race.UD, "Overall", null, null, true);

        Names.Apply(heroStats);

        var entries = heroStats.HeroStatsItemList.SelectMany(h => h.Stats).SelectMany(r => r.WinLossesOnMap).ToList();
        Assert.That(entries.Where(e => e.Map == EchoIslesFile).Select(e => e.MapName), Is.All.EqualTo("Echo Isles v2"));
        Assert.That(entries.Where(e => e.Map == AutumnLeavesLegacyKey).Select(e => e.MapName), Is.All.EqualTo("Autumn Leaves"));
        Assert.That(entries.Where(e => e.Map == "Overall").Select(e => e.MapName), Is.All.Null);
    }

    [Test]
    public void Apply_RaceStats_PatchAndLegacyEntries()
    {
        var stat = PlayerRaceOnMapVersusRaceRatio.Create("Player#123", 17);
        stat.AddMapWin(Race.HU, Race.UD, AutumnLeavesFile, 44, "Autumn Leaves", true, "1.36.1");
        stat.RaceWinsOnMap.AddWin(Race.HU, Race.UD, AutumnLeavesFile, 44, "Autumn Leaves", true);
        var view = PlayerRaceOnMapVersusRaceRatioView.Create(stat);

        Names.Apply(view);

        var entries = view.RaceWinsOnMapByPatch.Values.SelectMany(p => p).Concat(view.RaceWinsOnMap)
            .SelectMany(r => r.WinLossesOnMap).Where(e => e.Map == AutumnLeavesFile).ToList();
        Assert.That(entries, Is.Not.Empty);
        Assert.That(entries.Select(e => e.MapName), Is.All.EqualTo("Autumn Leaves v2"));
    }

    [Test]
    public void Apply_OverallRaceAndWinStat_KeepsOverallFirst_AndCoversLegacyList()
    {
        var stat = new OverallRaceAndWinStat(1200);
        stat.Apply("Overall", null, "Overall", Race.HU, Race.UD, true, "All");
        stat.Apply(EchoIslesFile, 1051, "Echo Isles 2.0", Race.HU, Race.UD, true, "All");
        stat.StatsPerModes.Add(new MapToRaceVsRaceRatio { Map = EchoIslesFile, MapId = 1051, MapName = "Echo Isles 2.0" });

        Names.Apply([stat]);

        var all = stat.PatchToStatsPerModes["All"];
        Assert.That(all[0].Map, Is.EqualTo("Overall"));
        Assert.That(all[0].MapName, Is.EqualTo("Overall"));
        Assert.That(all[1].MapName, Is.EqualTo("Echo Isles v2"));
        Assert.That(stat.StatsPerModes.Single().MapName, Is.EqualTo("Echo Isles v2"));
    }

    [Test]
    public void Apply_Matchups_ByTheirOwnMode()
    {
        var melee = new Matchup { GameMode = GameMode.GM_1v1, Map = EchoIslesFile, MapId = 1051, MapName = "Echo Isles 2.0" };
        var legion = new OnGoingMatchup { GameMode = GameMode.GM_LEGION_1v1_x20, Map = LegionTdFile, MapId = 5072, MapName = "Legion TD 11.0" };

        Names.Apply(new List<Matchup> { melee, legion });

        Assert.That(melee.MapName, Is.EqualTo("Echo Isles v2"));
        Assert.That(legion.MapName, Is.EqualTo("Legion TD 11.0"));
    }

    [Test]
    public void Apply_MatchupDetail_AndNulls()
    {
        var detail = new MatchupDetail { Match = new Matchup { GameMode = GameMode.FFA, MapId = 44, MapName = "Autumn Leaves" } };

        Names.Apply(detail);
        Names.Apply((MatchupDetail)null);
        Names.Apply((Matchup)null);
        Names.Apply(new MatchupDetail());

        Assert.That(detail.Match.MapName, Is.EqualTo("Autumn Leaves v2"));
    }
}
