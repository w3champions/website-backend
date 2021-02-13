using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Achievements {
    public interface IWinsNeededRule
    {
        long WinsNeeded { get; }
        Race Race { get; }
    }
}