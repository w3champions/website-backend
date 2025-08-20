using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Abstractions;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Rewards.Portraits;

namespace W3ChampionsStatisticService.Rewards.Modules;

public class PortraitRewardModule(
    IPersonalSettingsRepository personalSettingsRepo,
    IPortraitRepository portraitRepo,
    ILogger<PortraitRewardModule> logger) : IRewardModule
{
    private readonly IPersonalSettingsRepository _personalSettingsRepo = personalSettingsRepo;
    private readonly IPortraitRepository _portraitRepo = portraitRepo;
    private readonly ILogger<PortraitRewardModule> _logger = logger;

    public string ModuleId => "portrait_reward";
    public string ModuleName => "Portrait Reward";
    public string Description => "Grants special portrait pictures to users";
    public bool SupportsParameters => true;

    public async Task<RewardApplicationResult> Apply(RewardContext context)
    {
        var portraitIds = GetPortraitIds(context.Parameters);
        if (!portraitIds.Any())
        {
            return new RewardApplicationResult
            {
                Success = false,
                Message = "No portrait IDs specified"
            };
        }

        var settings = await _personalSettingsRepo.Load(context.UserId) ?? new PersonalSetting(context.UserId);
        var existingPortraitIds = settings.SpecialPictures?.Select(p => p.PictureId).ToHashSet() ?? new HashSet<int>();

        var newPortraits = portraitIds
            .Where(id => !existingPortraitIds.Contains(id))
            .Select(id => new SpecialPicture(id, ""))
            .ToList();

        if (newPortraits.Any())
        {
            var specialPictures = (settings.SpecialPictures ?? new SpecialPicture[0])
                .Concat(newPortraits)
                .ToArray();

            settings.UpdateSpecialPictures(specialPictures);
            await _personalSettingsRepo.Save(settings);

            _logger.LogInformation("Added {Count} portraits to user {UserId}", newPortraits.Count, context.UserId);
        }

        return new RewardApplicationResult
        {
            Success = true,
            Message = $"Granted {newPortraits.Count} new portraits",
            ResultData = new Dictionary<string, object>
            {
                ["portrait_ids"] = portraitIds,
                ["new_portraits"] = newPortraits.Select(p => p.PictureId).ToList()
            }
        };
    }

    public async Task<RewardRevocationResult> Revoke(RewardContext context)
    {
        var portraitIds = GetPortraitIds(context.Parameters);
        if (!portraitIds.Any())
        {
            return new RewardRevocationResult
            {
                Success = false,
                Message = "No portrait IDs specified"
            };
        }

        var settings = await _personalSettingsRepo.Load(context.UserId);
        if (settings?.SpecialPictures != null)
        {
            var portraitIdSet = portraitIds.ToHashSet();
            var remainingPictures = settings.SpecialPictures
                .Where(p => !portraitIdSet.Contains(p.PictureId))
                .ToArray();

            settings.UpdateSpecialPictures(remainingPictures);
            await _personalSettingsRepo.Save(settings);

            _logger.LogInformation("Revoked {Count} portraits from user {UserId}", portraitIds.Count, context.UserId);
        }

        return new RewardRevocationResult
        {
            Success = true,
            Message = $"Revoked {portraitIds.Count} portraits"
        };
    }

    public Task<ValidationResult> ValidateParameters(Dictionary<string, object> parameters)
    {
        var result = new ValidationResult { IsValid = true };

        if (!parameters.ContainsKey("portraitIds"))
        {
            result.IsValid = false;
            result.Errors.Add("portraitIds parameter is required");
        }
        else
        {
            var portraitIds = GetPortraitIds(parameters);
            if (!portraitIds.Any())
            {
                result.IsValid = false;
                result.Errors.Add("At least one portrait ID must be specified");
            }
        }

        return Task.FromResult(result);
    }

    public Dictionary<string, ParameterDefinition> GetParameterDefinitions()
    {
        return new Dictionary<string, ParameterDefinition>
        {
            ["portraitIds"] = new ParameterDefinition
            {
                Name = "portraitIds",
                Type = "int[]",
                Required = true,
                Description = "List of portrait IDs to grant"
            }
        };
    }

    private List<int> GetPortraitIds(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("portraitIds", out var value))
            return new List<int>();

        return value switch
        {
            List<int> intList => intList,
            int[] intArray => intArray.ToList(),
            _ => new List<int>()
        };
    }
}