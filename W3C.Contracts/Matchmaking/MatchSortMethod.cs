using System.Runtime.Serialization;

namespace W3C.Contracts.Matchmaking;

public enum MatchSortMethod
{
    [EnumMember(Value = "startTimeDescending")]
    StartTimeDescending,

    [EnumMember(Value = "startTimeAscending")]
    StartTimeAscending, // Currently not used, but adding it for future use

    [EnumMember(Value = "mmrDescending")]
    MmrDescending,

    [EnumMember(Value = "mmrAscending")]
    MmrAscending,  // Currently not used, but adding it for future use
}
