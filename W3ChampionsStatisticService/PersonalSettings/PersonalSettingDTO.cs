using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace W3ChampionsStatisticService.PersonalSettings;

public class PersonalSettingsDTO
{
    [MaxLength(300)]
    public string ProfileMessage { get; set; }
    public string Twitch { get; set; }
    public string Youtube { get; set; }
    public string Twitter { get; set; }
    public string Trovo { get; set; }
    public string Douyu { get; set; }

    [MaxLength(50)]
    public string HomePage { get; set; }
    public string Country { get; set; }
    public string CountryCode { get; set; }
    public List<string> ChatColors { get; set; }
    public List<string> ChatIcons { get; set; }
    public string SelectedChatColor { get; set; }
    public List<string> SelectedChatIcons { get; set; }
    public AkaSettings AliasSettings { get; set; }
}
