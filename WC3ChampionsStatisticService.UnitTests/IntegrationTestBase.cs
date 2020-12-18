using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class IntegrationTestBase
    {
        protected readonly MongoClient MongoClient = new MongoClient("mongodb://localhost:27017/");
        // protected readonly MongoClient MongoClient = new MongoClient("mongodb://176.28.16.249:3512/");

        [SetUp]
        public async Task Setup()
        {
            await MongoClient.DropDatabaseAsync("W3Champions-Statistic-Service");
        }

        protected async Task InsertMatchEvents(List<MatchFinishedEvent> newEvents)
        {
            foreach (var ev in newEvents)
            {
                await InsertMatchEvent(ev);
            }
        }

        protected async Task InsertMatchEvent(MatchFinishedEvent newEvent)
        {
            var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
            var mongoDatabase = database;
            var mongoCollection = mongoDatabase.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));
            await mongoCollection.FindOneAndReplaceAsync(
                (Expression<Func<MatchFinishedEvent, bool>>) (ev => ev.match.id == newEvent.match.id),
                newEvent,
                new FindOneAndReplaceOptions<MatchFinishedEvent> {IsUpsert = true});
        }

        protected async Task InsertMatchStartedEvent(MatchStartedEvent newEvent)
        {
            var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
            var mongoDatabase = database;
            var mongoCollection = mongoDatabase.GetCollection<MatchStartedEvent>(nameof(MatchStartedEvent));
            await mongoCollection.FindOneAndReplaceAsync(
                (Expression<Func<MatchStartedEvent, bool>>) (ev => ev.match.id == newEvent.match.id),
                newEvent,
                new FindOneAndReplaceOptions<MatchStartedEvent> {IsUpsert = true});
        }

        protected async Task InsertRankChangedEvent(RankingChangedEvent newEvent)
        {
            var database = MongoClient.GetDatabase("W3Champions-Statistic-Service");
            var mongoDatabase = database;
            var mongoCollection = mongoDatabase.GetCollection<RankingChangedEvent>(nameof(RankingChangedEvent));
            await mongoCollection.FindOneAndReplaceAsync(
                (Expression<Func<RankingChangedEvent, bool>>) (ev => ev.id == newEvent.id),
                newEvent,
                new FindOneAndReplaceOptions<RankingChangedEvent> {IsUpsert = true});
        }
    }
}