using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Authorization
{

    [ApiController]
    [Route("api/oauth")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IBlizzardAuthenticationService _authenticationService;

        public AuthorizationController(IBlizzardAuthenticationService authenticationService)
        {
            _authenticationService = authenticationService;
        }

        [HttpGet("token")]
        public async Task<IActionResult> GetToken([FromQuery] string code, [FromQuery] string redirectUri)
        {
            var token = await _authenticationService.GetToken(code, redirectUri);
            return token == null ? (IActionResult) Unauthorized("Sorry H4ckerb0i") : Ok(token);
        }

        [HttpGet("battleTag")]
        public async Task<IActionResult> GetUserInfo([FromQuery] string bearer)
        {
            var userInfo = await _authenticationService.GetUser(bearer);
            return userInfo == null ? (IActionResult) Unauthorized("Sorry H4ckerb0i") : Ok(userInfo);
        }
    }
}