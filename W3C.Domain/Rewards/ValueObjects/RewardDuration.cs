namespace W3C.Domain.Rewards.ValueObjects;

public class RewardDuration
{
    public DurationType Type { get; set; }
    public int Value { get; set; }
    
    public static RewardDuration Permanent() => new() { Type = DurationType.Permanent, Value = 0 };
    public static RewardDuration Days(int days) => new() { Type = DurationType.Days, Value = days };
    public static RewardDuration Months(int months) => new() { Type = DurationType.Months, Value = months };
    public static RewardDuration Years(int years) => new() { Type = DurationType.Years, Value = years };
}

public enum DurationType
{
    Permanent,
    Days,
    Months,
    Years
}