using System.Linq;
using System.Net.Http;
using System.Web;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminRepositoryUrlEncodingTests
{
    private const string Hostile = "battleTag&generateExplanation=true&iterationDepth=9#frag";

    [Test]
    public void QuerySmurfs_SendsAHostileIdentifierTypeAsOneEncodedValue_AndKeepsThePermissionDerivedFlag()
    {
        var request = AdminRepository.BuildQuerySmurfsRequest(Hostile, "Foo#1234", includeExplanation: false, iterationDepth: 1);

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(request.RequestUri!.Fragment, Is.Empty);
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        Assert.That(query.AllKeys, Is.EquivalentTo(new[] { "identifierType", "identifier", "generateExplanation", "iterationDepth" }));
        Assert.That(query["identifierType"], Is.EqualTo(Hostile));
        Assert.That(query["identifier"], Is.EqualTo("Foo#1234"));
        Assert.That(query["generateExplanation"], Is.EqualTo("false"));
        Assert.That(query["iterationDepth"], Is.EqualTo("1"));
    }

    [Test]
    public void QuerySmurfs_SendsAHostileIdentifierAsOneEncodedValue()
    {
        var request = AdminRepository.BuildQuerySmurfsRequest("battleTag", "a#b&c=d", includeExplanation: true, iterationDepth: 2);

        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        Assert.That(request.RequestUri.Fragment, Is.Empty);
        Assert.That(query["identifier"], Is.EqualTo("a#b&c=d"));
        Assert.That(query["generateExplanation"], Is.EqualTo("true"));
        Assert.That(query.AllKeys.Count(k => k == "generateExplanation"), Is.EqualTo(1));
    }

    [Test]
    public void ChatBans_EncodeTheCursorAndTheDeletedId()
    {
        var list = AdminRepository.BuildChatBansUrl(null, "x&query=y#z");
        var delete = AdminRepository.BuildDeleteChatBanUrl("a/b?c#d");

        Assert.That(list, Does.EndWith("?nextId=x%26query%3Dy%23z"));
        Assert.That(delete, Does.EndWith("/flo/globalChatBans/a%2Fb%3Fc%23d"));
    }
}
