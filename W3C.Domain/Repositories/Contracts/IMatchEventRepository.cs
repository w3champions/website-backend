﻿using MongoDB.Bson;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;

namespace W3C.Domain.Repositories
{
    public interface IMatchEventRepository
    {
        Task<List<MatchFinishedEvent>> Load(string lastObjectId,  int pageSize = 100);
        Task<List<MatchStartedEvent>> LoadStartedMatches();
        Task<bool> InsertIfNotExisting(MatchFinishedEvent matchFinishedEvent, int i = 0);
        Task<List<RankingChangedEvent>> CheckoutForRead();
        Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellationChanged();
        Task DeleteStartedEvent(ObjectId nextEventId);
    }
}