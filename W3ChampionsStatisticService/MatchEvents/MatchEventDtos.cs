using System.Collections.Generic;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.MatchEvents
{
    public class OverallScore
    {
        public int UNIT_SCORE { get; set; }
        public int HERO_SCORE { get; set; }
        public int RESOURCE_SCORE { get; set; }
        public int TOTAL_SCORE { get; set; }
    }

    public class UnitScore
    {
        public int UNITS_PRODUCED { get; set; }
        public int UNITS_KILLED { get; set; }
        public int STRUCTURES_PRODUCED { get; set; }
        public int STRUCTURES_RAZED { get; set; }
        public int LARGEST_ARMY { get; set; }
    }

    public class HeroRaw
    {
        public string icon { get; set; }
        public int level { get; set; }
    }

    public class HeroScore
    {
        public int HEROES_KILLED { get; set; }
        public int ITEMS_OBTAINED { get; set; }
        public int MERCS_HIRED { get; set; }
        public int EXP_GAINED { get; set; }
        public int STRONGER_HEROES { get; set; }
    }

    public class ResourceScore
    {
        public int GOLD_COLLECTED { get; set; }
        public int LUMBER_COLLECTED { get; set; }
        public int RESOURCES_RECVD { get; set; }
        public int RESOURCES_SENT { get; set; }
        public int TECH_PERCENTAGE { get; set; }
        public int GOLD_UPKEEP_LOST { get; set; }
    }

    public class PlayerRaw
    {
        public int id { get; set; }
        public int raceId { get; set; }
        public int gamePlayerId { get; set; }
        public int playerColor { get; set; }
        public int teamIndex { get; set; }
        public string toonName { get; set; }
        public bool won { get; set; }
        public bool isAi { get; set; }
        public string battleTag { get; set; }
        public string clanName { get; set; }
        public string avatarId { get; set; }
        public OverallScore overallScore { get; set; }
        public UnitScore unitScore { get; set; }
        public List<HeroRaw> heroes { get; set; }
        public HeroScore heroScore { get; set; }
        public ResourceScore resourceScore { get; set; }
    }

    public class MapInfoRaw
    {
        public int elapsedGameTimeTotalSeconds { get; set; }
        public int elapsedGameTimeTotalHours { get; set; }
        public int elapsedGameTimeMinutes { get; set; }
        public int elapsedGameTimeSeconds { get; set; }
        public string elapsedSec { get; set; }
        public string name { get; set; }
        public string mapFile { get; set; }
        public bool isReplay { get; set; }
        public string replayFile { get; set; }
        public int difficulty { get; set; }
        public int campaignIndex { get; set; }
        public int missionIndex { get; set; }
        public string gameType { get; set; }
    }

    public class Data
    {
        public bool localPlayerWon { get; set; }
        public bool isHDModeEnabled { get; set; }
        public int localPlayerRace { get; set; }
        public string gameName { get; set; }
        public string gameId { get; set; }
        public List<PlayerRaw> players { get; set; }
        public MapInfoRaw mapInfo { get; set; }
        public long id { get; set; }
    }

    public class MatchFinishedEvent : Versionable
    {
        public string type { get; set; }
        public Data data { get; set; }
    }
}