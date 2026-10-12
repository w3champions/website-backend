using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

[TestFixture]
public class ReplayExportTicketStoreTests
{
    [Test]
    public void Exists_KeepsTheTicket()
    {
        var store = new ReplayExportTicketStore(new MemoryCache(new MemoryCacheOptions()));
        var (ticket, _) = store.Create(new ReplayExportTicket { RequestedBy = "admin#1" });

        Assert.IsTrue(store.Exists(ticket));
        Assert.AreEqual("admin#1", store.Take(ticket)?.RequestedBy);
        Assert.IsFalse(store.Exists(ticket));
        Assert.IsNull(store.Take(ticket));
        Assert.IsFalse(store.Exists(null));
    }

    [Test]
    public async Task Take_GivesTheTicketToOnlyOneOfManyRequests()
    {
        var store = new ReplayExportTicketStore(new MemoryCache(new MemoryCacheOptions()));
        for (var round = 0; round < 50; round++)
        {
            var (ticket, _) = store.Create(new ReplayExportTicket());

            var taken = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Take(ticket))));

            Assert.AreEqual(1, taken.Count(t => t != null));
        }
    }
}
