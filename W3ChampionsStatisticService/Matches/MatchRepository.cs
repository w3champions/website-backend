using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
    {
        public MatchRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task Insert(Matchup matchup)
        {
            return Upsert(matchup, m => m.Id == matchup.Id);
        }

        public async Task<List<Matchup>> LoadFor(
            string playerId,
            string opponentId = null,
            GameMode gameMode = GameMode.Undefined,
            int pageSize = 100,
            int offset = 0)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<Matchup>(nameof(Matchup));

            if (string.IsNullOrEmpty(opponentId))
            {
                return await mongoCollection
                    .Find(m => (gameMode == GameMode.Undefined || m.GameMode == gameMode)
                               && m.Teams
                                   .Any(t => t.Players
                                       .Any(p => p.Id.Equals(playerId))))
                    .SortByDescending(s => s.StartTime)
                    .Skip(offset)
                    .Limit(pageSize)
                    .ToListAsync();
            }

            // this is fing hacky, fix when there is a good idea
            return await mongoCollection
                .Find(m =>  (gameMode == GameMode.Undefined || m.GameMode == gameMode) &&
                            (  m.Teams[0].Players[0].Id == playerId && m.Teams[1].Players[0].Id == opponentId
                            || m.Teams[1].Players[0].Id == playerId && m.Teams[0].Players[0].Id == opponentId

                            || m.Teams[0].Players[0].Id == playerId && m.Teams[1].Players[0].Id == opponentId
                            || m.Teams[0].Players[0].Id == playerId && m.Teams[1].Players[1].Id == opponentId
                            || m.Teams[0].Players[1].Id == playerId && m.Teams[1].Players[0].Id == opponentId
                            || m.Teams[0].Players[1].Id == playerId && m.Teams[1].Players[1].Id == opponentId

                            || m.Teams[1].Players[0].Id == playerId && m.Teams[0].Players[0].Id == opponentId
                            || m.Teams[1].Players[0].Id == playerId && m.Teams[0].Players[1].Id == opponentId
                            || m.Teams[1].Players[1].Id == playerId && m.Teams[0].Players[0].Id == opponentId
                            || m.Teams[1].Players[1].Id == playerId && m.Teams[0].Players[1].Id == opponentId
                             ))
                .SortByDescending(s => s.StartTime)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();
        }

        public Task<long> Count()
        {
            return CreateCollection<Matchup>().CountDocumentsAsync(x => true);
        }

        public Task<long> CountFor(
            string playerId,
            string opponentId = null,
            GameMode gameMode = GameMode.Undefined)
        {
            var mongoCollection = CreateCollection<Matchup>();
            if (string.IsNullOrEmpty(opponentId))
            {
                return mongoCollection.CountDocumentsAsync(m =>
                    (gameMode == GameMode.Undefined || m.GameMode == gameMode) &&
                    m.Teams
                        .Any(t => t.Players
                            .Any(p => p.Id.Equals(playerId))));
            }

            // this is fing hacky, fix when there is a good idea
            return mongoCollection.CountDocumentsAsync(m =>
                (gameMode == GameMode.Undefined || m.GameMode == gameMode) &&
                (  m.Teams[0].Players[0].Id == playerId && m.Teams[1].Players[0].Id == opponentId
                || m.Teams[1].Players[0].Id == playerId && m.Teams[0].Players[1].Id == opponentId

                || m.Teams[0].Players[0].Id == playerId && m.Teams[1].Players[0].Id == opponentId
                || m.Teams[0].Players[0].Id == playerId && m.Teams[1].Players[1].Id == opponentId
                || m.Teams[0].Players[1].Id == playerId && m.Teams[1].Players[0].Id == opponentId
                || m.Teams[0].Players[1].Id == playerId && m.Teams[1].Players[1].Id == opponentId

                || m.Teams[1].Players[0].Id == playerId && m.Teams[0].Players[0].Id == opponentId
                || m.Teams[1].Players[0].Id == playerId && m.Teams[0].Players[1].Id == opponentId
                || m.Teams[1].Players[1].Id == playerId && m.Teams[0].Players[0].Id == opponentId
                || m.Teams[1].Players[1].Id == playerId && m.Teams[0].Players[1].Id == opponentId
                ));
        }

        public async Task<MatchupDetail> LoadDetails(string id)
        {
            var originalMatch = await LoadFirst<MatchFinishedEvent>(t => t.match.id == id);
            var match = await LoadFirst<Matchup>(t => t.Id == id);

            return new MatchupDetail
            {
                Match = match,
                PlayerScores = originalMatch?.result?.players.Select(p => CreateDetail(p)).ToList()
            };
        }

        private PlayerScore CreateDetail(PlayerBlizzard playerBlizzard)
        {
            return new PlayerScore(
                playerBlizzard.battleTag,
                playerBlizzard.overallScore,
                playerBlizzard.unitScore,
                playerBlizzard.heroes,
                playerBlizzard.heroScore,
                playerBlizzard.resourceScore);
        }

        public async Task<List<Matchup>> Load(
            GameMode gameMode = GameMode.Undefined,
            int offset = 0,
            int pageSize = 100,
            int gateWay = 10)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<Matchup>(nameof(Matchup));

            var events = await mongoCollection.Find(m => m.GateWay == gateWay
                                                         && (gameMode == GameMode.Undefined || m.GameMode == gameMode))
                .SortByDescending(s => s.StartTime)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }
    }
}