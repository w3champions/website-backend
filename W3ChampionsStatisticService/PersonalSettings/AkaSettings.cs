using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class AkaSettings
    {
        
        public bool showAka { get; set; }
        public bool showW3info { get; set; }
        public bool showLiquipedia { get; set; }

        public static AkaSettings Default()
        {
            return new AkaSettings()
            {
                showAka = true,
                showW3info = true,
                showLiquipedia = true
            };
        }
    }
}
