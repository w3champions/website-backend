namespace W3ChampionsStatisticService.Ladder;

public class League(int id, int order, string name, int division)
{
    public int Division { get; set; } = division;
    public int Id { get; set; } = id;
    public string Name { get; set; } = name;
    public int Order { get; set; } = order;
}
