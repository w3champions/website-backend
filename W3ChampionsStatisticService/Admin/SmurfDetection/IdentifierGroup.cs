using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin.SmurfDetection;

public class SmurfDetectionIdentifierGroup
{
    public BattleTagLoginStatistics[] fromBattleTags { get; set; }
    public string identifier { get; set; }
    public BattleTagLoginStatistics[] toBattleTags { get; set; }
}