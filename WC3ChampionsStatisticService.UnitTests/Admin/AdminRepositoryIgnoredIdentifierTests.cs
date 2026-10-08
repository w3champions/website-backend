using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.SmurfDetection;

namespace WC3ChampionsStatisticService.Tests.Admin;

[TestFixture]
public class AdminRepositoryIgnoredIdentifierTests
{
    [Test]
    public async Task AddRequestUsesSingularRouteAndValueField()
    {
        var request = AdminRepository.BuildAddIgnoredIdentifierRequest("ipAddress", "1.2.3.4", "shared cafe", "Admin#1");

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith("/admin/smurf-detection/ignored-identifier"));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);

        var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
        Assert.That(body["type"]!.Value<string>(), Is.EqualTo("ipAddress"));
        Assert.That(body["value"]!.Value<string>(), Is.EqualTo("1.2.3.4"));
        Assert.That(body["reason"]!.Value<string>(), Is.EqualTo("shared cafe"));
        Assert.That(body["author"]!.Value<string>(), Is.EqualTo("Admin#1"));
        Assert.That(body.ContainsKey("identifier"), Is.False);
    }

    [Test]
    public async Task DeleteRequestUsesSingularRouteWithIdInJsonBody()
    {
        var request = AdminRepository.BuildDeleteIgnoredIdentifierRequest("abc123");

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Delete));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith("/admin/smurf-detection/ignored-identifier"));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);

        var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
        Assert.That(body["id"]!.Value<string>(), Is.EqualTo("abc123"));
    }

    [Test]
    public void GetRequestUsesSingularRouteWithEncodedQuery()
    {
        var request = AdminRepository.BuildGetIgnoredIdentifierRequest("battleTag", "Grubby#1234");

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith("/admin/smurf-detection/ignored-identifier"));
        Assert.That(request.RequestUri.Query, Does.Contain("type=battleTag"));
        Assert.That(request.RequestUri.Query, Does.Contain("identifier=Grubby%231234"));
        Assert.That(request.Headers.Contains("x-admin-secret"), Is.True);
    }

    [Test]
    public void AddResponseIsUnwrappedFromNewIdentifierEnvelope()
    {
        var content = "{\"message\":\"Ignored identifier added\",\"newIdentifier\":{\"_id\":\"abc123\",\"type\":\"ipAddress\",\"identifier\":\"1.2.3.4\",\"author\":\"Admin#1\",\"reason\":\"shared cafe\"}}";

        var identifier = AdminRepository.ParseAddIgnoredIdentifierResponse(content);

        Assert.That(identifier.id, Is.EqualTo("abc123"));
        Assert.That(identifier.type, Is.EqualTo("ipAddress"));
        Assert.That(identifier.identifier, Is.EqualTo("1.2.3.4"));
        Assert.That(identifier.author, Is.EqualTo("Admin#1"));
        Assert.That(identifier.reason, Is.EqualTo("shared cafe"));
    }

    [Test]
    public void AddResponseWithEmptyBodyYieldsNull()
    {
        Assert.That(AdminRepository.ParseAddIgnoredIdentifierResponse(""), Is.Null);
    }

    [Test]
    public void IgnoredIdentifierIdIsReadFromUnderscoreId()
    {
        var parsed = JsonConvert.DeserializeObject<IgnoredIdentifier>("{\"_id\":\"abc123\",\"type\":\"ipAddress\",\"identifier\":\"1.2.3.4\"}");

        Assert.That(parsed!.id, Is.EqualTo("abc123"));
    }

    [Test]
    public void AddResponseWithoutNewIdentifierYieldsNull()
    {
        Assert.That(AdminRepository.ParseAddIgnoredIdentifierResponse("{\"message\":\"x\"}"), Is.Null);
    }

    [Test]
    public void GetResponseWithLiteralNullBodyYieldsNull()
    {
        Assert.That(AdminRepository.ParseGetIgnoredIdentifierResponse("null"), Is.Null);
    }

    [Test]
    public void IgnoredIdentifierSerializesAsIdForTheFrontend()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new IgnoredIdentifier { id = "abc123", type = "ipAddress" });
        var body = JObject.Parse(json);

        Assert.That(body["id"]!.Value<string>(), Is.EqualTo("abc123"));
        Assert.That(body.ContainsKey("_id"), Is.False);
    }
}
