using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using W3C.Contracts.Replay;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Replay;

[TestFixture]
public class ReplayTests
{
    private string _json = @"
        {
        ""players"":[
            {
                ""id"": 1,
                ""name"":""p1#1"",
                ""team"":0,
                ""color"":1
            },
            {
                ""id"": 2,
                ""name"":""p2#2"",
                ""team"":1,
                ""color"":2
            }
        ],
        ""messages"":[
            {
                ""from_player"":1,
                ""scope"":{
                    ""type"":""all""
                },
                ""content"":""p1 all""
            },
            {
                ""from_player"":2,
                ""scope"":{
                    ""type"":""allies""
                },
                ""content"":""p2 allies""
            },
            {
                ""from_player"":2,
                ""scope"":{
                    ""type"":""player"",
                    ""id"":1
                },
                ""content"":""p2 to p1""
            }
        ]
        }
    ";

    [Test]
    public void DeserializeChatsData()
    {
        var result = JsonConvert.DeserializeObject<ReplayChatsData>(_json);

        var players = new List<ReplayChatsPlayerInfo>();
        players.Add(new ReplayChatsPlayerInfo {
            Id = 1,
            Name = "p1#1",
            Team = 0,
            Color = 1
        });
        players.Add(new ReplayChatsPlayerInfo {
            Id = 2,
            Name = "p2#2",
            Team = 1,
            Color = 2
        });
        var messages = new List<ReplayChatsMessage>();
        messages.Add(new ReplayChatsMessage {
            FromPlayer = 1,
            Scope = new ReplayChatsScope {
                Type = ReplayChatsScopeType.All,
            },
            Content = "p1 all",
        });
        messages.Add(new ReplayChatsMessage {
            FromPlayer = 2,
            Scope = new ReplayChatsScope {
                Type = ReplayChatsScopeType.Allies,
            },
            Content = "p2 allies",
        });
        messages.Add(new ReplayChatsMessage {
            FromPlayer = 2,
            Scope = new ReplayChatsScope {
                Type = ReplayChatsScopeType.Player,
                Id = 1
            },
            Content = "p2 to p1",
        });
        result.Should().BeEquivalentTo(new ReplayChatsData {
            Players = players,
            Messages = messages,
        });
    }
}
