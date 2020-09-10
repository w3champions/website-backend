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
        private readonly RegistrationHandler _registrationHandler;

        public AuthorizationController(
            IBlizzardAuthenticationService authenticationService,
            ITwitchAuthenticationService twitchAuthenticationService,
            RegistrationHandler registrationHandler)
        {
            _authenticationService = authenticationService;
            _twitchAuthenticationService = twitchAuthenticationService;
            _registrationHandler = registrationHandler;
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
            var userInfo = await _registrationHandler.GetUserOrRegister(bearer);

            return userInfo == null ? (IActionResult) Unauthorized("Sorry H4ckerb0i") : Ok(userInfo);
        }

        [HttpGet("twitch")]
        public async Task<IActionResult> GetTwitchToken()
        {
            var token = await _twitchAuthenticationService.GetToken();
            return Ok(token);
        }
    }
}