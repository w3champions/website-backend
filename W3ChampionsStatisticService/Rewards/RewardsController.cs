using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Rewards.Portraits;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Rewards
{
    [Route("api/rewards")]
    [ApiController]
    public class RewardsController : ControllerBase
    {
        private PortraitCommandHandler _portraitCommandHandler;
        private PortraitRepository _portraitRepository;

        public RewardsController(
            PortraitRepository portraitRepository,
            PortraitCommandHandler portraitCommandHandler)
        {
            _portraitRepository = portraitRepository;
            _portraitCommandHandler = portraitCommandHandler;
        }

        [HttpPut("portraits")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PutPortraits([FromBody] PortraitsCommand command)
        {
            await _portraitCommandHandler.UpsertSpecialPortraits(command);
            return Ok();
        }

        [HttpDelete("portraits")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeletePortraits([FromBody] PortraitsCommand command)
        {
            await _portraitCommandHandler.DeleteSpecialPortraits(command);
            return Ok();
        }

        [HttpGet("portrait-definitions")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetPortraitDefinitions()
        {
            var portraits = await _portraitCommandHandler.GetPortraitDefinitions();
            return Ok(portraits);
        }

        [HttpPut("portrait-definitions")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DefinePortraits([FromBody] PortraitsDefinitionCommand command)
        {
            await _portraitCommandHandler.AddPortraitDefinition(command);
            return Ok();
        }

        [HttpDelete("portrait-definitions")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> RemovePortraits([FromBody] PortraitsDefinitionCommand command)
        {
            await _portraitCommandHandler.RemovePortraitDefinition(command);
            return Ok();
        }

        [HttpGet("portrait-groups")]
        public async Task<IActionResult> GetPortraitGroups()
        {
            await _portraitRepository.LoadDistinctPortraitGroups();
            return Ok();
        }
    }
}
