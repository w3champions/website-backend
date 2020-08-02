using System.Linq;

namespace W3ChampionsStatisticService.Matches
{
    public class MapName
    {
        public MapName(string map)
        {
            var mapNampe = map.Split("/").Last().Replace(".w3x", "").Replace("_lv", "").Substring(3);
            Name = ParseForLegacy(mapNampe);
        }

        private string ParseForLegacy(string mapName)
        {
            switch (mapName)
            {
                case "_1v1_autumnleaves_anon": return "_1v1_autumnleaves_anon";
                case "_1v1_terenasstand_anon": return "terenasstand";
                case "_1v1_lastrefuge_anon": return "lastrefuge";
                case "_1v1_northernisles_anon": return "northernisles";
                case "_1v1_amazonia_anon": return "amazonia";
                case "_1v1_echoisles_anon": return "echoisles";
                case "_1v1_concealedhill_anon": return "concealedhill";
                case "_1v1_twistedmeadows_anon": return "twistedmeadows";

                case "_gnollwood_anon": return "gnollwood";
                case "_avalanche_anon": return "avalanche";
                case "_battlegrounds_anon": return "battleground";
                case "_cherryville_anon": return "cherryville";
                case "_circleoffallenheroes_anon": return "circleoffallenheroes";
                case "_deadlock_lv_anon": return "deadlock";
                case "_feralas_lv_anon": return "feralas";
                case "_fullscaleassault_anon": return "fullscaleassault";
                case "_goldrush_anon": return "goldrush";
                case "_goldshire_anon": return "goldshire";
                case "_goleminthemist_lv_anon": return "golemsinthemist";
                case "_hillsbradcreek_anon": return "hillsbradcreek";
                case "_losttemple_lv_anon": return "losttemple";
                case "_marketsquare_anon": return "marketsquare";
                case "_mur'galoasis_lv_anon": return "murguloasis";
                case "_nerubianpassage_anon": return "nerubianpassage";
                case "_northernfelwood_anon": return "northernfelwood";
                case "_northshire_lv_anon": return "northshire";
                case "_sanctuary_lv_anon": return "sanctuary";
                case "_tidewaterglades_lv_anon": return "tidewaterglades";
                case "_turtlerock_anon": return "turtlerock";
                case "_twilightruins_anon": return "twilightruins";

                case "_ffa_marketsquare_anon_cd": return "_ffa_marketsquare_anon";
                case "_ffa_deathrose_anon_cd": return "_ffa_deathrose_anon";
                case "_ffa_fountainofmanipulation_anon_cd": return "_ffa_fountainofmanipulation_anon";
                case "_ffa_anarchycastle_anon_cd": return "_ffa_anarchycastle_anon";
                case "_ffa_silverpineforest_anon_cd": return "_ffa_silverpineforest_anon";
                case "_ffa_neoncity_anon_cd": return "_ffa_neoncity_anon";
                case "_ffa_harvestofsorrow_anon_cd": return "_ffa_harvestofsorrow_anon";
                case "_ffa_twilightruins_anon_cd": return "_ffa_twilightruins_anon";
                case "_ffa_deadlock lv_anon_cd": return "_ffa_deadlock lv_anon";
                case "_ffa_sanctuary lv_anon_cd": return "_ffa_sanctuary lv_anon";
                case "_ffa_rockslide_anon_cd": return "_ffa_rockslide_anon";
                case "_ffa_ferocity_anon_cd": return "_ffa_ferocity_anon";

                default: return mapName;
            }
        }

        public string Name { get; }
    }
}