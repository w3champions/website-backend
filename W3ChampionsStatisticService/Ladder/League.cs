namespace W3ChampionsStatisticService.Ladder;

public class League
{
    public League(int id, int order, string name, int division)
    {
        Division = division;
        Id = id;
        Name = name;
        Order = order;
    }

    public int Division { get; set; }
    public int Id { get; set; }
    public string Name { get; set; }
    public int Order { get; set; }
}
