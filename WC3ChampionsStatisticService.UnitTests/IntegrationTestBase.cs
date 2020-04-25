using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;

namespace WC3ChampionsStatisticService.UnitTests
{
    public class IntegrationTestBase
    {
        protected readonly MongoClient MongoClient = new MongoClient("mongodb://176.28.16.249:3512/");

        [SetUp]
        public async Task Setup()
        {
            await MongoClient.DropDatabaseAsync("W3Champions-Statistic-Service");
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
    }
}