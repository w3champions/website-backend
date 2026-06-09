using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Auth;

[TestFixture]
public class JwtTests
{
    private string _jwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJiYXR0bGVUYWciOiJtb2Rtb3RvIzI4MDkiLCJpc0FkbWluIjoiVHJ1ZSIsIm5hbWUiOiJtb2Rtb3RvIn0.0rJooIabRqj_Gt0fuuW5VP6ICdV1FJfwRJYuhesou7rPqE9HWZRewm12bd4iWusa4lcYK6vp5LCr6fBj4XUc2iQ4Bo9q3qtu54Rwc-eH2m-_7VqJE6D3yLm7Gcre0NE2LHZjh7qA5zHQn5kU_ugOmcovaVJN_zVEM1wRrVwR6mkNDwIwv3f_A_3AQOB8s0rin0MS4950DnFkmM0CLQ-MMzwFHg_kKgiStSiAp-2Mlu5SijGUx8keM3ArjOj7Kplk_wxjPCkjplIfAHb5qXBpdcO5exXD7UJwETqUHu4NgH-9-GWzPPNCW5BMfzPV-BMiO1sESEb4JZUZqTSJCnAG2d1mx_yukDHR_8ZSd-rB5en2WzOdN1Fjds_M0u5BvnAaLQOzz69YURL4mnI-jiNpFNokRWYjzG-_qEVJTRtUugiCipT6SMs3SlwWujxXsNSZZU0LguOuAh4EqF9ST7m_ttOcZvg5G1RLOy6A1QzWVG06Byw-7dZvMpoHrMSqjlNcJk7XtDamAVDyUNpjrqlu_I17U5DN6f8evfBtngsSgpjeswy6ccul10HRNO210I7VejGOmEsxnIDWyF-5p-UIuOaTgMiXhElwSpkIaLGQJXHFXc859UjvqC7jSRnPWpRlYRo7UpKmCJ59fgK-SzZlbp27gN_1uhk18eEWrenn6ew";

    [Test]
    public void GetToken()
    {
        var w3CAuthenticationService = new W3CAuthenticationService();
        var userByToken1 = w3CAuthenticationService.GetUserByToken(_jwt, false);

        Assert.AreEqual("modmoto#2809", userByToken1.BattleTag);
    }

    // ── Permission-vocabulary drift between identification-service and this service ────────
    //
    // identification-service grows its EPermission vocabulary independently and emits granted
    // permissions in the JWT `permissions` claim (a JSON array, expanded into one claim per element on
    // read). A value this service's enum does not yet contain must be IGNORED, not throw: FromJWT has no
    // try/catch, so an Enum.Parse throw propagates to every GetUserByToken caller and 401s the user on
    // ALL admin/permission/acting-player/hub endpoints (two of those filters mask the real reason).

    /// <summary>
    /// Builds a JWT signed with a freshly-generated RSA keypair, mirroring the claim shape the
    /// identification-service emits — crucially the <c>permissions</c> claim as a serialized JSON array
    /// (<see cref="JsonClaimValueTypes.JsonArray"/>), which the handler expands into one claim per
    /// element on read. Returns the token plus the matching public-key PEM that
    /// <see cref="W3CUserAuthenticationDto.FromJWT"/> validates against.
    /// </summary>
    private static (string jwt, string publicKeyPem) CreateSignedJwt(string battleTag, bool isAdmin, IEnumerable<string> permissions)
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("battleTag", battleTag),
                new Claim("isAdmin", isAdmin.ToString()),
                new Claim("name", battleTag.Split('#')[0]),
                new Claim("permissions", JsonSerializer.Serialize(permissions.ToList()), JsonClaimValueTypes.JsonArray),
            },
            signingCredentials: signingCredentials,
            expires: DateTime.UtcNow.AddDays(7));

        return (new JwtSecurityTokenHandler().WriteToken(token), publicKeyPem);
    }

    [Test]
    public void FromJWT_TokenWithPermissionUnknownToThisService_StillAuthenticates()
    {
        // Models the next id-service-only permission this service hasn't mirrored yet.
        var (jwt, publicKeyPem) = CreateSignedJwt("moderator#123", isAdmin: true,
            new[] { "Moderation", "SomeFuturePermissionNotInEnum" });

        var result = W3CUserAuthenticationDto.FromJWT(jwt, publicKeyPem, validateLifetime: false);

        Assert.IsNotNull(result, "A user holding a permission unknown to this service must still authenticate");
        Assert.AreEqual("moderator#123", result.BattleTag);
        Assert.IsTrue(result.IsAdmin);
        Assert.IsTrue(result.Permissions.Contains(EPermission.Moderation), "Known permissions must be retained");
        Assert.AreEqual(1, result.Permissions.Count, "The unrecognized permission must be dropped, not throw");
    }

    [Test]
    public void FromJWT_TokenWithOnlyKnownPermissions_ParsesAll()
    {
        // Control: proves the JSON-array claim is expanded into per-element claims and all known
        // permissions parse. Passes both before and after the tolerant-parse fix.
        var (jwt, publicKeyPem) = CreateSignedJwt("admin#1", isAdmin: true,
            new[] { "Moderation", "Warnings" });

        var result = W3CUserAuthenticationDto.FromJWT(jwt, publicKeyPem, validateLifetime: false);

        Assert.IsNotNull(result);
        CollectionAssert.AreEquivalent(new[] { EPermission.Moderation, EPermission.Warnings }, result.Permissions);
    }
}
