using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles;

public class PlayerOverallStatsHandler(
    IPlayerRepository playerRepository,
    IPersonalSettingsRepository personalSettingsRepository) : IReadModelHandler
{
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        foreach (var playerRaw in nextEvent.match.players)
        {
            var player = await _playerRepository.LoadPlayerProfile(playerRaw.battleTag)
                            ?? PlayerOverallStats.Create(playerRaw.battleTag);
            player.RecordWin(
                playerRaw.race,
                nextEvent.match.season, playerRaw.won);
            await _playerRepository.UpsertPlayer(player);

            await UpdateLocation(playerRaw);
        }
    }

    public async Task UpdateLocation(PlayerMMrChange player)
    {
        if (!string.IsNullOrEmpty(player.country))
        {
            var setting = await _personalSettingsRepository.LoadOrCreate(player.battleTag) ?? new PersonalSetting(player.battleTag);

            setting.Location = player.country;

            await _personalSettingsRepository.Save(setting);
        }
    }
}
