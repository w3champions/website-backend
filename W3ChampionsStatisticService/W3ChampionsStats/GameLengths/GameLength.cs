namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

public class GameLength
{
    public void AddGame()
    {
        Games++;
    }

    public int Seconds { get; set; }
    public int Games { get; set; }
}
