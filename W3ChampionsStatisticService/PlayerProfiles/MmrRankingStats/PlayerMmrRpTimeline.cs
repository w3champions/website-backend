using System;
using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;

public class PlayerMmrRpTimeline(string battleTag, Race race, GateWay gateWay, int season, GameMode gameMode) : IIdentifiable
{
    public string Id { get; set; } = $"{season}_{battleTag}_@{gateWay}_{race}_{gameMode}";
    public List<MmrRpAtDate> MmrRpAtDates { get; set; } = new List<MmrRpAtDate>();

    public void UpdateTimeline(MmrRpAtDate mmrRpAtDate)
    {
        // Empty?
        if (MmrRpAtDates.Count == 0)
        {
            MmrRpAtDates.Add(mmrRpAtDate);
            return;
        }

        // Insert at last pos?
        int index = MmrRpAtDates.Count - 1;
        if (MmrRpAtDates[index].Date <= mmrRpAtDate.Date)
        {
            if (CheckDateExists(index, mmrRpAtDate))
            {
                HandleDateExists(index, mmrRpAtDate);
                return;
            }
            MmrRpAtDates.Add(mmrRpAtDate);
            return;
        }

        // Insert at first pos?
        index = 0;
        {
            if (MmrRpAtDates[index].Date >= mmrRpAtDate.Date)
            {
                if (CheckDateExists(index, mmrRpAtDate))
                {
                    HandleDateExists(index, mmrRpAtDate);
                    return;
                }
                MmrRpAtDates.Insert(index, mmrRpAtDate);
                return;
            }
        }

        int bsIndex = MmrRpAtDates.BinarySearch(mmrRpAtDate);
        if (bsIndex < 0)
            bsIndex = ~bsIndex;

        // Check if date already exists
        // if so, use the mmrRpAtDate with later Date
        for (int i = 0; i <= 1; i++)
        {
            index = bsIndex - i;
            if (index >= 0 && index < MmrRpAtDates.Count)
            {
                if (CheckDateExists(index, mmrRpAtDate))
                {
                    HandleDateExists(index, mmrRpAtDate);
                    return;
                }
            }
        }
        MmrRpAtDates.Insert(bsIndex, mmrRpAtDate);
    }

    private Boolean CheckDateExists(int oldId, MmrRpAtDate mmrRpAtDate)
    {
        var neighbour = MmrRpAtDates[oldId];
        if (mmrRpAtDate.HasSameYearMonthDayAs(neighbour))
        {
            return true;
        }
        return false;
    }

    private void HandleDateExists(int oldId, MmrRpAtDate mmrRpAtDate)
    {
        var neighbour = MmrRpAtDates[oldId];
        //if (mmrRpAtDate.Mmr > neighbour.Mmr)
        if (mmrRpAtDate.Date > neighbour.Date)
        {
            MmrRpAtDates[oldId] = mmrRpAtDate;
        }
    }
}

public class MmrRpAtDate(int mmr, double? rp, DateTimeOffset date) : IComparable
{
    public int Mmr { get; set; } = mmr;
    public double? Rp { get; set; } = rp;
    public DateTimeOffset Date { get; set; } = date;

    public Boolean HasSameYearMonthDayAs(MmrRpAtDate mRAT2)
    {
        return this.Date.Year == mRAT2.Date.Year &&
            this.Date.Month == mRAT2.Date.Month &&
            this.Date.Day == mRAT2.Date.Day;
    }

    public int CompareTo(object obj)
    {
        DateTimeOffset mmrTime_obj = ((MmrRpAtDate)obj).Date;
        if (this.Date < mmrTime_obj)
            return -1;
        if (this.Date > mmrTime_obj)
            return 1;
        return 0;
    }
}
