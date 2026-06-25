using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3C.Contracts.Replay;

public class ReplayChatsData
{
    public List<ReplayChatsPlayerInfo> Players { get; set; }

    public List<ReplayChatsMessage> Messages { get; set; }

    // Pause/resume/leave events parsed from the replay, ordered by Time.
    public List<ReplayGameEvent> Events { get; set; }
}

public class ReplayChatsPlayerInfo
{
    public int Id { get; set; }

    public string Name { get; set; }

    public int Team { get; set; }

    public int Color { get; set; }
}

public class ReplayChatsMessage
{
    // Real elapsed time in milliseconds (advances even while the game is paused).
    [JsonProperty("time")]
    public int Time { get; set; }

    // In-game clock in milliseconds (frozen while the game is paused).
    [JsonProperty("game_time")]
    public int GameTime { get; set; }

    [JsonProperty("from_player")]
    public int FromPlayer { get; set; }

    public ReplayChatsScope Scope { get; set; }

    public string Content { get; set; }
}

public class ReplayChatsScope
{
    public ReplayChatsScopeType Type { get; set; }

    public int? Id { get; set; }
}

public enum ReplayChatsScopeType
{
    All,
    Allies,
    Observers,
    Player,
}

public class ReplayGameEvent
{
    public ReplayGameEventType Type { get; set; }

    // Real elapsed time in milliseconds (advances even while the game is paused).
    [JsonProperty("time")]
    public int Time { get; set; }

    // In-game clock in milliseconds (frozen while the game is paused).
    [JsonProperty("game_time")]
    public int GameTime { get; set; }

    [JsonProperty("player_id")]
    public int PlayerId { get; set; }

    // Only present for Leave events. A snake_case LeaveReason string emitted by
    // the replay service (e.g. "disconnect", "lost_buildings", "won"). Passed
    // through verbatim; the website remaps it to human-readable text.
    [JsonProperty("leave_reason")]
    public string LeaveReason { get; set; }
}

public enum ReplayGameEventType
{
    Pause,
    Resume,
    Leave,
}
