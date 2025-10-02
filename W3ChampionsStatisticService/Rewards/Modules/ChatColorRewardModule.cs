using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Abstractions;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using Serilog;

namespace W3ChampionsStatisticService.Rewards.Modules;

public class ChatColorRewardModule(
    IPersonalSettingsRepository personalSettingsRepo) : IRewardModule
{
    private readonly IPersonalSettingsRepository _personalSettingsRepo = personalSettingsRepo;

    public string ModuleId => "chat_color_reward";
    public string ModuleName => "Chat Color Reward";
    public string Description => "Grants chat color customization to users";
    public bool SupportsParameters => true;

    public async Task<RewardApplicationResult> Apply(RewardContext context)
    {
        Log.Information("Applying chat color reward to user {UserId}: {Parameters}", context.UserId, context.Parameters);

        var colorId = GetColorId(context.Parameters);

        if (string.IsNullOrWhiteSpace(colorId))
        {
            return new RewardApplicationResult
            {
                Success = false,
                Message = "colorId parameter is required"
            };
        }

        var settings = await _personalSettingsRepo.Load(context.UserId) ?? new PersonalSetting(context.UserId);

        if (settings.ChatColors == null)
        {
            settings.ChatColors = new List<ChatColor>();
        }

        var chatColor = new ChatColor(colorId);
        if (!settings.ChatColors.Any(c => c.ColorId == colorId))
        {
            settings.ChatColors.Add(chatColor);

            // Auto-select the color if no color is currently selected
            var autoSelected = settings.SelectedChatColor == null || string.IsNullOrWhiteSpace(settings.SelectedChatColor?.ColorId);
            if (autoSelected)
            {
                settings.SelectedChatColor = chatColor;
                Log.Information("Auto-selected chat color {ColorId} for user {UserId} (no color previously selected)", colorId, context.UserId);
            }

            await _personalSettingsRepo.Save(settings);

            Log.Information("Added chat color {ColorId} to user {UserId}", colorId, context.UserId);

            return new RewardApplicationResult
            {
                Success = true,
                Message = $"Granted chat color: {colorId}",
                ResultData = new Dictionary<string, object>
                {
                    ["color_id"] = colorId,
                    ["auto_selected"] = autoSelected
                }
            };
        }
        else
        {
            Log.Information("User {UserId} already has chat color {ColorId}", context.UserId, colorId);
            return new RewardApplicationResult
            {
                Success = true,
                Message = $"User already has chat color: {colorId}"
            };
        }
    }

    public async Task<RewardRevocationResult> Revoke(RewardContext context)
    {
        Log.Information("Revoking chat color reward from user {UserId}: {Parameters}", context.UserId, context.Parameters);

        var colorId = GetColorId(context.Parameters);
        if (string.IsNullOrWhiteSpace(colorId))
        {
            return new RewardRevocationResult
            {
                Success = false,
                Message = "colorId parameter is required"
            };
        }

        var settings = await _personalSettingsRepo.Load(context.UserId);
        if (settings?.ChatColors != null && settings.ChatColors.Any(c => c.ColorId == colorId))
        {
            settings.ChatColors.RemoveAll(c => c.ColorId == colorId);

            // Remove from selected color if it was selected
            if (settings.SelectedChatColor != null && settings.SelectedChatColor.ColorId == colorId)
            {
                settings.SelectedChatColor = null;
                Log.Information("Removed {ColorId} from selected chat color for user {UserId}", colorId, context.UserId);
            }

            await _personalSettingsRepo.Save(settings);

            Log.Information("Revoked chat color {ColorId} from user {UserId}", colorId, context.UserId);
        }

        return new RewardRevocationResult
        {
            Success = true,
            Message = $"Revoked chat color: {colorId}"
        };
    }

    public Task<ValidationResult> ValidateParameters(Dictionary<string, object> parameters)
    {
        var result = new ValidationResult { IsValid = true };

        if (!parameters.ContainsKey("colorId") || string.IsNullOrWhiteSpace(GetColorId(parameters)))
        {
            result.IsValid = false;
            result.Errors.Add("colorId parameter is required");
        }

        return Task.FromResult(result);
    }

    public Dictionary<string, ParameterDefinition> GetParameterDefinitions()
    {
        return new Dictionary<string, ParameterDefinition>
        {
            ["colorId"] = new ParameterDefinition
            {
                Name = "colorId",
                Type = "string",
                Required = true,
                Description = "Admin-defined identifier for the chat color (resolved by frontend)"
            }
        };
    }

    private string GetColorId(Dictionary<string, object> parameters)
    {
        return parameters.TryGetValue("colorId", out var value) ? value?.ToString() : null;
    }

}
