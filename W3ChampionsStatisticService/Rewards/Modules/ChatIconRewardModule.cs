using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Rewards.Abstractions;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using Serilog;

namespace W3ChampionsStatisticService.Rewards.Modules;

public class ChatIconRewardModule(
    IPersonalSettingsRepository personalSettingsRepo) : IRewardModule
{
    private readonly IPersonalSettingsRepository _personalSettingsRepo = personalSettingsRepo;

    public string ModuleId => "chat_icon_reward";
    public string ModuleName => "Chat Icon Reward";
    public string Description => "Grants chat icon customization to users";
    public bool SupportsParameters => true;

    public async Task<RewardApplicationResult> Apply(RewardContext context)
    {
        Log.Information("Applying chat icon reward to user {UserId}: {Parameters}", context.UserId, context.Parameters);

        var iconId = GetIconId(context.Parameters);

        if (string.IsNullOrWhiteSpace(iconId))
        {
            return new RewardApplicationResult
            {
                Success = false,
                Message = "iconId parameter is required"
            };
        }

        var settings = await _personalSettingsRepo.Load(context.UserId) ?? new PersonalSetting(context.UserId);

        if (settings.ChatIcons == null)
        {
            settings.ChatIcons = new List<ChatIcon>();
        }

        var chatIcon = new ChatIcon(iconId);
        if (!settings.ChatIcons.Any(i => i.IconId == iconId))
        {
            settings.ChatIcons.Add(chatIcon);

            // Auto-select the icon if less than 3 icons are currently selected
            if (settings.SelectedChatIcons == null)
            {
                settings.SelectedChatIcons = new List<ChatIcon>();
            }

            var autoSelected = false;
            if (settings.SelectedChatIcons.Count < 3 && !settings.SelectedChatIcons.Any(i => i.IconId == iconId))
            {
                settings.SelectedChatIcons.Add(chatIcon);
                autoSelected = true;
                Log.Information("Auto-selected chat icon {IconId} for user {UserId} (has {SelectedCount} icons selected)",
                    iconId, context.UserId, settings.SelectedChatIcons.Count - 1);
            }

            await _personalSettingsRepo.Save(settings);

            Log.Information("Added chat icon {IconId} to user {UserId}", iconId, context.UserId);

            return new RewardApplicationResult
            {
                Success = true,
                Message = $"Granted chat icon: {iconId}",
                ResultData = new Dictionary<string, object>
                {
                    ["icon_id"] = iconId,
                    ["auto_selected"] = autoSelected
                }
            };
        }
        else
        {
            Log.Information("User {UserId} already has chat icon {IconId}", context.UserId, iconId);
            return new RewardApplicationResult
            {
                Success = true,
                Message = $"User already has chat icon: {iconId}"
            };
        }
    }

    public async Task<RewardRevocationResult> Revoke(RewardContext context)
    {
        Log.Information("Revoking chat icon reward from user {UserId}: {Parameters}", context.UserId, context.Parameters);

        var iconId = GetIconId(context.Parameters);
        if (string.IsNullOrWhiteSpace(iconId))
        {
            return new RewardRevocationResult
            {
                Success = false,
                Message = "iconId parameter is required"
            };
        }

        var settings = await _personalSettingsRepo.Load(context.UserId);
        if (settings?.ChatIcons != null && settings.ChatIcons.Any(i => i.IconId == iconId))
        {
            settings.ChatIcons.RemoveAll(i => i.IconId == iconId);

            // Remove from selected icons if it was selected
            if (settings.SelectedChatIcons != null && settings.SelectedChatIcons.Any(i => i.IconId == iconId))
            {
                settings.SelectedChatIcons.RemoveAll(i => i.IconId == iconId);
                Log.Information("Removed {IconId} from selected chat icons for user {UserId}", iconId, context.UserId);
            }

            await _personalSettingsRepo.Save(settings);

            Log.Information("Revoked chat icon {IconId} from user {UserId}", iconId, context.UserId);
        }

        return new RewardRevocationResult
        {
            Success = true,
            Message = $"Revoked chat icon: {iconId}"
        };
    }

    public Task<ValidationResult> ValidateParameters(Dictionary<string, object> parameters)
    {
        var result = new ValidationResult { IsValid = true };

        if (!parameters.ContainsKey("iconId") || string.IsNullOrWhiteSpace(GetIconId(parameters)))
        {
            result.IsValid = false;
            result.Errors.Add("iconId parameter is required");
        }

        return Task.FromResult(result);
    }

    public Dictionary<string, ParameterDefinition> GetParameterDefinitions()
    {
        return new Dictionary<string, ParameterDefinition>
        {
            ["iconId"] = new ParameterDefinition
            {
                Name = "iconId",
                Type = "string",
                Required = true,
                Description = "Admin-defined identifier for the chat icon (used for translations and descriptions)"
            }
        };
    }

    private string GetIconId(Dictionary<string, object> parameters)
    {
        return parameters.TryGetValue("iconId", out var value) ? value?.ToString() : null;
    }
}
