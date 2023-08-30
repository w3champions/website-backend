using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class W3CAuthenticationService : IW3CAuthenticationService
    {
        private static readonly string JwtPublicKey = Regex.Unescape(Environment.GetEnvironmentVariable("JWT_PUBLIC_KEY") ?? "-----BEGIN PUBLIC KEY-----\nMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEA6N6yiNe392lG5W+4LKDD\nNOr53vvolpC7098x6tWbw0E3Jkg8n3Y8A1qC9+7tFYXV8I5UlQdT1Oy/BxbPuNR0\nS/zr93WeYkLCWlfh7yjFKwNbRSoWXL36lFhy85H+5HNGfjKpTm5HLTXKRH1P4lLk\n3Gfz0p84OXeumUs9cDRz7WSSEeGTpD4oA3qGgS18F2U394No/YfNIOyJCOzDRaN9\nMx8H2VcsOvZnGqeCWKtY+7fh1YQQqR2ebZb1eA0qziloxnXhI2sUXUnjK68YIV3d\nXaFhYuSsJQoXuHzIA1opcFkGhkQI+wVyLzaAPhWiU0MCvoRf+kxfmW8gaUdT+2ar\no2C2lXp5Y/0xyrl3w0bzinQ79n+PH0pixu00r4/892IksS5SexdZ1Ka5TaHdnWGR\njM1p1DmFqyKvm98wsoq4ZsgYVrMHOY3qDRdb4ss93HjgA5gh6q3rnLFdUC8T+FgL\nkwZIsRm4+a0by3xwglHgWBOu81Pzy4F1dQOV3C31cgLsMZvBW0D01I7F/Y5YFU1A\nlLgKocWLLDEnWMh+078H3PyRH9W3vuQGfD6CAfEu8jbETgZeZqiyR45yDGeyZlWE\nbtiZjF00dkblGb5z5BFRtYHwL2Cfi6XJnby77NYHPTUH1GrfdL+sp7QEDe9k/4h6\nsYbv9oAYja2AuGxDba1MJHUCAwEAAQ==\n-----END PUBLIC KEY-----\n");

        public W3CUserAuthenticationDto GetUserByToken(string jwt, bool validateLifetime = true)
        {
            return W3CUserAuthenticationDto.FromJWT(jwt, JwtPublicKey, validateLifetime);
        }
    }

    public interface IW3CAuthenticationService
    {
        W3CUserAuthenticationDto GetUserByToken(string jwt, bool validateLifetime = true);
    }

    public class W3CUserAuthenticationDto
    {
        public static W3CUserAuthenticationDto FromJWT(string jwt, string publicKey, bool validateLifetime)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = validateLifetime,
                ValidateTokenReplay = false,
                ValidateActor = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(rsa)
            };

            var handler = new JwtSecurityTokenHandler();
            var claims = handler.ValidateToken(jwt, validationParameters, out _);
            var btag = claims.Claims.First(c => c.Type == "battleTag").Value;
            var isAdmin = Boolean.Parse(claims.Claims.First(c => c.Type == "isAdmin").Value);
            var name = claims.Claims.First(c => c.Type == "name").Value;

            return new W3CUserAuthenticationDto
            {
                Name = name,
                BattleTag = btag,
                IsAdmin = isAdmin
            };
        }

        public string BattleTag { get; set; }
        public string Name { get; set; }
        public bool IsAdmin { get; set; }
    }
}