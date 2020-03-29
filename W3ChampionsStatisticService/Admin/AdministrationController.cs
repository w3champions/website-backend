using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Admin
{
    [ApiController]
    [Route("api/administration")]
    public class AdministrationController : ControllerBase
    {
        private readonly AdminCommandHandler _adminCommandHandler;

        public AdministrationController(AdminCommandHandler adminCommandHandler)
        {
            _adminCommandHandler = adminCommandHandler;
        }

        [HttpPut("reset")]
        public async Task<IActionResult> GetMatches(
            string readModelType,
            string readModelHandler,
            string authorization)
        {
            if (authorization != "C6ACB38C-3334-4196-8ECD-207A93600EB1") return Unauthorized("Sorry H4ckerb0i");
            await _adminCommandHandler.ResetReadModel(readModelType, readModelHandler);
            return Ok();
        }
    }
}