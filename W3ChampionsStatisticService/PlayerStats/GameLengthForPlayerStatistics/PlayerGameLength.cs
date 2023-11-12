using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Contracts.GameObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;
public class PlayerGameLength : IIdentifiable
{
    public string Id => compoundId(BattleTag, Season);
    public Dictionary<string, PlayerGameLengthStat> PlayerGameLengthIntervalByOpponentRace { get; set; }
    [JsonIgnore]
    public Dictionary<string, List<int>> GameLengthsByOpponentRace { get; set; }
    public Dictionary<string, int> AverageGameLengthByOpponentRace { get; set; }
    public string BattleTag { get; set; }
    public int Season { get; set; }
    public void AddGameLength(int seconds, int opponentRace)
    {
        HandleGameLengthIntervals(seconds, opponentRace);
        InsertIntoRaceDictionary(seconds, opponentRace);
        CalculateAverage(opponentRace);
        HandleAllGamesLengths(seconds);
    }

  private void HandleGameLengthIntervals(int seconds, int opponentRace)
  {
    var opponentRaceString = opponentRace.ToString();
    
    if (!PlayerGameLengthIntervalByOpponentRace.ContainsKey(opponentRaceString)) {
      PlayerGameLengthIntervalByOpponentRace.Add(opponentRaceString, PlayerGameLengthStat.Create());
    }

    var raceTotalInt = (int) Race.Total;
    var raceTotalString = raceTotalInt.ToString();
    if (!PlayerGameLengthIntervalByOpponentRace.ContainsKey(raceTotalString)) {
      PlayerGameLengthIntervalByOpponentRace.Add(raceTotalString, PlayerGameLengthStat.Create());
    }

    PlayerGameLengthIntervalByOpponentRace[opponentRaceString].Apply(seconds);
    PlayerGameLengthIntervalByOpponentRace[raceTotalString].Apply(seconds);
  }

  public static string compoundId(string battleTag, int Season) {
      return battleTag + "_" + Season;
    }

    private void HandleAllGamesLengths(int seconds) {
        var total = (int) Race.Total;
        InsertIntoRaceDictionary(seconds, total);
        CalculateAverage(total);
    }

    private void CalculateAverage(int opponentRace)
    {
        var opponentRaceString = opponentRace.ToString();
        var average = GameLengthsByOpponentRace[opponentRaceString].Average();
        if (!AverageGameLengthByOpponentRace.ContainsKey(opponentRaceString))
        {
          AverageGameLengthByOpponentRace.Add(opponentRaceString, 0);
        }
        AverageGameLengthByOpponentRace[opponentRaceString] = (int)average;
    }

    private void InsertIntoRaceDictionary(int seconds, int opponentRace)
    {
        var opponentRaceString = opponentRace.ToString();
        if (!GameLengthsByOpponentRace.ContainsKey(opponentRaceString))
        {
          GameLengthsByOpponentRace.Add(opponentRaceString, new List<int>());
        }
        GameLengthsByOpponentRace[opponentRaceString].Add(seconds);
    }
}
