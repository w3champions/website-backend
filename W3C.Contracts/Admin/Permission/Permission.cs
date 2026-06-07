namespace W3C.Contracts.Admin.Permission;

public class Permission
{
    public string Id => BattleTag;
    public string BattleTag { get; set; }
    public string Description { get; set; }
    public EPermission[] Permissions { get; set; }
    public string Author { get; set; }
}

public enum EPermission
{
    Permissions = 0,
    Moderation = 1,
    Queue = 2,
    Logs = 3,
    Maps = 4,
    Tournaments = 5,
    Content = 6,
    Proxies = 7,
    SmurfCheckerQuery = 8,
    SmurfCheckerQueryExplanation = 9,
    SmurfCheckerAdministration = 10,
    Warnings = 11,
}
