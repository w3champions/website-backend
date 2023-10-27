namespace W3C.Contracts.Admin.Moderation;

public class LoungeMute
{
    public string battleTag { get; set; }
    public string endDate { get; set; }
    public string author { get; set;}
}

public class LoungeMuteResponse : LoungeMute
{
    public string insertDate { get; set; }
}
