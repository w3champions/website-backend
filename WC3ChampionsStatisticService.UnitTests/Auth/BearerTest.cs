using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class BearerTest
{

    [Test]
    public void GetTokenFailed()
    {
        Assert.Throws<SecurityTokenValidationException>(() => BearerHasPermissionFilter.GetToken(StringValues.Empty));
    }

    [Test]
    public void GetTokenSuccess()
    {
        var result = BearerHasPermissionFilter.GetToken(new StringValues("Bearer fake_token_value"));
        Assert.AreEqual("fake_token_value", result);
    }
}
