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
    public void Peek_KeepsTheTicket()
    {
        var store = new ReplayExportTicketStore(new MemoryCache(new MemoryCacheOptions()));
        var (ticket, _) = store.Create(new ReplayExportTicket { RequestedBy = "admin#1" });

        Assert.AreEqual("admin#1", store.Peek(ticket)?.RequestedBy);
        Assert.AreEqual("admin#1", store.Take(ticket)?.RequestedBy);
        Assert.IsNull(store.Peek(ticket));
        Assert.IsNull(store.Take(ticket));
        Assert.IsNull(store.Peek(null));
    }

    [Test]
    public void Create_ReplacesTheUnusedTicketOfTheSameUser()
    {
        var store = new ReplayExportTicketStore(new MemoryCache(new MemoryCacheOptions()));
        var (first, _) = store.Create(new ReplayExportTicket { RequestedBy = "buyer#1" });
        var (other, _) = store.Create(new ReplayExportTicket { RequestedBy = "buyer#2" });
        var (second, _) = store.Create(new ReplayExportTicket { RequestedBy = "buyer#1" });

        Assert.IsNull(store.Peek(first));
        Assert.IsNotNull(store.Peek(other));
        Assert.IsNotNull(store.Take(second));

        // A used ticket is gone already: the next one replaces nothing.
        var (third, _) = store.Create(new ReplayExportTicket { RequestedBy = "buyer#1" });
        Assert.IsNotNull(store.Take(third));
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
