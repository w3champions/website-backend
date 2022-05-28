﻿using W3C.Domain.MatchmakingService.MatchmakingContracts;

namespace W3C.Domain.UpdateService.Contracts
{
    public class MapFileData
    {
        public string Id { get; set; }

        public int MapId { get; set; }

        public string FilePath { get; set; }

        public GameMap MetaData { get; set; }
    }
}
