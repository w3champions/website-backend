using System.ComponentModel.DataAnnotations;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class ProfileCommand
    {
        [MaxLength(300)]
        public string Value { get; set; }
    }
}