using System.ComponentModel.DataAnnotations;

namespace W3ChampionsStatisticService.Clans.Commands
{
    public class CreateClanDto
    {
        [Required]
        [RegularExpression(@"^\w{3,30}$", ErrorMessage = "Name must be between 3 and 30 numerical characters")]
        public string ClanName { get; set; }

        [Required]
        [RegularExpression(@"^\w{2,5}$", ErrorMessage = "Abbreviation must be between 1 and 5 numerical characters")]
        public string ClanAbbrevation { get; set; }
    }
}