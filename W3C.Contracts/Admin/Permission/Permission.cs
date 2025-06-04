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
    Permissions,
    Moderation,
    Queue,
    Logs,
    Maps,
    Tournaments,
    Content,
    Proxies,
    SmurfCheckerQuery,
    SmurfCheckerQueryExplanation,
    SmurfCheckerAdministration,
}
