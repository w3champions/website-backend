using MongoDB.Driver;
using System.Collections.Generic;
using System;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace W3ChampionsStatisticService.Services
{
    public class MatchesProvider : MongoDbRepositoryBase
    {
        public static CachedData<List<PersonalSetting>> personalSettingsCache;
        public static Dictionary<GameMode, CachedData<List<Matchup>>> _matchesCache;
        public static Dictionary<GameMode, CachedData<long>> _matchesCountCache;
        public MatchesProvider(MongoClient mongoClient) : base(mongoClient)
        {
            _matchesCache = new Dictionary<GameMode, CachedData<List<Matchup>>>();
            _matchesCountCache = new Dictionary<GameMode, CachedData<long>>();
            foreach (GameMode gm in Enum.GetValues(typeof(GameMode)))
            {
                _matchesCache.Add(gm, new CachedData<List<Matchup>>(() => FetchMatchDataSync(gm), TimeSpan.FromMinutes(1)));
                _matchesCountCache.Add(gm, new CachedData<long>(() => FetchMatchCountSync(gm), TimeSpan.FromMinutes(1)));
            }

        }

        public List<Matchup> FetchMatchDataSync(GameMode gm)
        {
            try
            {
                List<Matchup> matchups = FetchMatchData(gm).GetAwaiter().GetResult();
                return matchups;
            }
            catch
            {
                return new List<Matchup>();
            }
        }



        private Task<List<Matchup>> FetchMatchData(GameMode gm)
        {
            GameMode gameMode = gm;

            GateWay gateWay = GateWay.Undefined;
            string map = "Overall";
            int minMmr = 0;
            int maxMmr = 3000;
            int offset = 0;
            int pageSize = 500;
            return Load(gateWay, gm, offset, pageSize, map, minMmr, maxMmr);
        }
        public long FetchMatchCountSync(GameMode gm)
        {
            try
            {
                return FetchMatchCount(gm).GetAwaiter().GetResult();

            }
            catch
            {
                return 0;
            }
        }


        private Task<long> FetchMatchCount(GameMode gm)
        {

            GateWay gateWay = GateWay.Undefined;
            string map = "Overall";
            int minMmr = 0;
            int maxMmr = 3000;
            return Count(gateWay, gm, map, minMmr, maxMmr);
        }

        private Task<List<Matchup>> Load(
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            int offset = 0,
            int pageSize = 100,
            string map = "Overall",
            int minMmr = 0,
            int maxMmr = 3000)
        {
            var mongoCollection = CreateCollection<Matchup>();
            return mongoCollection
                .Find(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                    && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                    && (map == "Overall" || m.Map == map))
                .SortByDescending(s => s.EndTime)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();
        }

        private Task<long> Count(
            GateWay gateWay = GateWay.Undefined,
            GameMode gameMode = GameMode.Undefined,
            string map = "Overall",
            int minMmr = 0,
            int maxMmr = 3000)
        {
            return CreateCollection<Matchup>().CountDocumentsAsync(m =>
                    (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                    && (gateWay == GateWay.Undefined || m.GateWay == gateWay)
                    && (map == "Overall" || m.Map == map));
        }

        public List<Matchup> GetMatches(
            int offset = 0,
            int pageSize = 100,
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall",
            int minMmr = 0,
            int maxMmr = 3000)
        {
            List<Matchup> matches = new List<Matchup>();
            if (pageSize > 100) pageSize = 100;
            if (offset < 500 && (offset + pageSize) < 501 && map == "Overall" && minMmr == 0 && maxMmr == 3000)
            {
                matches = _matchesCache[gameMode].GetCachedData().Skip(offset).Take(pageSize).ToList();
            }
            else
            {
                matches = Load(gateWay, gameMode, offset, pageSize, map, minMmr, maxMmr).GetAwaiter().GetResult();
            }
            

            return  matches;
        }
        public long GetCount(
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall",
            int minMmr = 0,
            int maxMmr = 3000)
        {
            List<Matchup> matches = new List<Matchup>();
            long count = 0;
            if (map == "Overall" && minMmr == 0 && maxMmr == 3000)
            {
                count = _matchesCountCache[gameMode].GetCachedData();
            }
            else
            {
                count = Count(gateWay, gameMode, map, minMmr, maxMmr).GetAwaiter().GetResult();
            }


            return count;
        }
    }
    }
