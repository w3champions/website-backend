using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;

namespace WC3ChampionsStatisticService.Tests.MatchmakingService;

[TestFixture]
public class ActiveGameModeTests
{
    [Test]
    public void Deserializes_progressionStartSeason_from_matchmaking_payload()
    {
        var json = @"[{""id"":1,""name"":""1v1"",""type"":""1on1"",""teamCount"":2,""teamSize"":1,""progressionStartSeason"":42,""maps"":[]}]";

        var modes = JsonConvert.DeserializeObject<List<ActiveGameMode>>(json);

        modes.Should().HaveCount(1);
        modes[0].ProgressionStartSeason.Should().Be(42);
    }

    [Test]
    public void ProgressionStartSeason_is_null_when_absent()
    {
        var json = @"[{""id"":1,""name"":""1v1"",""type"":""1on1"",""teamCount"":2,""teamSize"":1,""maps"":[]}]";

        var modes = JsonConvert.DeserializeObject<List<ActiveGameMode>>(json);

        modes[0].ProgressionStartSeason.Should().BeNull();
    }

    [Test]
    public void ProgressionStartSeason_is_null_when_explicitly_null()
    {
        var json = @"[{""id"":1,""name"":""1v1"",""type"":""1on1"",""teamCount"":2,""teamSize"":1,""progressionStartSeason"":null,""maps"":[]}]";

        var modes = JsonConvert.DeserializeObject<List<ActiveGameMode>>(json);

        modes[0].ProgressionStartSeason.Should().BeNull();
    }
}
