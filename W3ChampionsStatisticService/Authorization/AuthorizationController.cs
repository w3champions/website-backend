using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Authorization
{

    [ApiController]
    [Route("api/oauth")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IBlizzardAuthenticationService _authenticationService;

        private readonly ITwitchAuthenticationService _twitchAuthenticationService;

        private OAuthToken _cachedToken { get; set; }

        public AuthorizationController(IBlizzardAuthenticationService authenticationService, ITwitchAuthenticationService twitchAuthenticationService)
        {
            _authenticationService = authenticationService;
            _twitchAuthenticationService = twitchAuthenticationService;
        }

        [HttpGet("token")]
        public async Task<IActionResult> GetBlizzardToken([FromQuery] string code, [FromQuery] string redirectUri)
        {
            var token = await _authenticationService.GetToken(code, redirectUri);
            return token == null ? (IActionResult)Unauthorized("Sorry H4ckerb0i") : Ok(token);
        }

        [HttpGet("battleTag")]
        public async Task<IActionResult> GetUserInfo([FromQuery] string bearer)
        {
            var userInfo = await _authenticationService.GetUser(bearer);
            return userInfo == null ? (IActionResult)Unauthorized("Sorry H4ckerb0i") : Ok(userInfo);
        }

        [HttpGet("twitch")]
        public async Task<IActionResult> GetTwitchToken()
        {
            const string CLIENT_ID = "38ac0gifyt5khcuq23h2p8zpcqosbc";
            const string CLIENT_SECRET = "0kec9qsb8otc3q0ibs3w2cjtiwaiez";

           
            var token = await _twitchAuthenticationService.GetToken(CLIENT_ID, CLIENT_SECRET);
            return Ok(token);
        }
    }
}