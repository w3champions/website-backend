using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchEventRepository
    {
        Task InsertAsync(IList<MatchFinishedEvent> events);
        Task<IList<MatchFinishedEvent>> LoadAsync(DateTimeOffset? now = null, int pageSize = 100);
    }
}