 using System.Collections.Generic;
 using System.Linq;
 using System.Threading.Tasks;
 using W3ChampionsStatisticService.Ports;
 using System;
 using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
 using System.Text.RegularExpressions;

 namespace W3ChampionsStatisticService.Services;

 public class W3StatsService
 {

   private readonly IW3StatsRepo _w3StatsRepo;

   public W3StatsService(IW3StatsRepo w3StatsRepo)
     {
         _w3StatsRepo = w3StatsRepo;
     }

   public async Task<List<OverallRaceAndWinStat>> LoadNRaceVsRaceStats(int n = 3)
     {
         var overallRaceAndWinStats = await _w3StatsRepo.LoadRaceVsRaceStats();
         var nRaceAndWinStats = new List<OverallRaceAndWinStat>();
         var nPatchNames = new HashSet<String>();

         foreach(var overallRaceAndWinPerStat in overallRaceAndWinStats)
         {
             var recentOverallRaceAndWinPerStat = new OverallRaceAndWinStat(overallRaceAndWinPerStat.MmrRange);
             var patchNames = overallRaceAndWinPerStat.PatchToStatsPerModes.Keys.ToList();

             if (nPatchNames.Count == 0)
             {
                 nPatchNames = GetNPatchNames(patchNames, n, true);
             }
             var nPatchToStatsPerModes = overallRaceAndWinPerStat.PatchToStatsPerModes.Where(kv => nPatchNames.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
             recentOverallRaceAndWinPerStat.PatchToStatsPerModes = nPatchToStatsPerModes;
             nRaceAndWinStats.Add(recentOverallRaceAndWinPerStat);
         }
         return nRaceAndWinStats;
     }

     private HashSet<String> GetNPatchNames(List<String> allVersionPatchNames, int n = 3, bool includeAll = true)
     {
         // Define a regular expression pattern for valid version numbers
         var validVersionNamePattern = @"^\d+(\.\d+)*$";

         // Filter out invalid version numbers (Eg: "All")
         var validVersionNames = allVersionPatchNames
         .Where(v => Regex.IsMatch(v, validVersionNamePattern))
         .ToList();
         var invalidVersionNames = allVersionPatchNames
         .Where(v => !Regex.IsMatch(v, validVersionNamePattern))
         .ToList();

         var sortedVersions = validVersionNames
             .Select(Version.Parse) // Convert strings to Version objects
             .OrderByDescending(v => v)     // Sort versions in descending order
             .Take(n)
             .Select(v => v.ToString())
             .ToList();

         var nPatchNames = new HashSet<string>(sortedVersions);
         nPatchNames.UnionWith(invalidVersionNames);

         return nPatchNames;
     }

 }