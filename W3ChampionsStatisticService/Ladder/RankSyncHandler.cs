using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankSyncHandler : IAsyncUpdatable
    {
        private readonly IRankRepository _rankRepository;
        private readonly IMatchEventRepository _matchEventRepository;
        private readonly TrackingService _trackingService;

        public RankSyncHandler(
            IRankRepository rankRepository,
            IMatchEventRepository matchEventRepository,
            TrackingService trackingService
            )
        {
            _rankRepository = rankRepository;
            _matchEventRepository = matchEventRepository;
            _trackingService = trackingService;
        }

        public async Task Update()
        {
            var events = await _matchEventRepository.CheckoutForRead();
            var rankingChangedEvent = events.FirstOrDefault();
            if (rankingChangedEvent == null) return;

            var loadLeagueConstellation = await _rankRepository.LoadLeagueConstellation(rankingChangedEvent.season);

            var ranks = new List<Rank>();

            foreach (var changedEvent in events)
            {
                var oneLeagueRanked = rankingChangedEvent.ranks.OrderByDescending(r => r.rp);
                var ranksParsed = CreateRanks(
                    changedEvent.league,
                    oneLeagueRanked,
                    loadLeagueConstellation,
                    changedEvent.season,
                    changedEvent.gateway,
                    changedEvent.gameMode);

                ranks.AddRange(ranksParsed);
            }

            await _rankRepository.InsertRanks(ranks);
        }

        private IEnumerable<Rank> CreateRanks(
            int league,
            IEnumerable<RankRaw> ranks,
            List<LeagueConstellation> loadLeagueConstellation,
            int season,
            GateWay gateWay,
            GameMode gameMode)
        {
            int i = 0;
            foreach (var rank in ranks)
            {
                i++;
                var findLeague = FindLeague(league, loadLeagueConstellation, season, gateWay, gameMode);
                yield return new Rank(rank.battleTags,
                    findLeague,
                    i + 1, (int) rank.rp, gateWay, gameMode, season);
            }
        }

        private League FindLeague(
            int leagueId,
            List<LeagueConstellation> loadLeagueConstellation,
            int season,
            GateWay gateWay,
            GameMode gameMode)
        {
            var leagueConstellation = loadLeagueConstellation.SingleOrDefault(l =>
                l.Gateway == gateWay
                && l.Season == season
                && l.GameMode == gameMode);
            var league = leagueConstellation?.Leagues?.SingleOrDefault(l => l.Id == leagueId);
            if (league != null) return league;

            _trackingService?.TrackException(new Exception(), $"A League was not found for {leagueId} season: {season} gate:{gateWay} mode:{gameMode} ");
            return new League(0, 0, "NotFound", 0);
        }
    }
}