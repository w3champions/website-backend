
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Admin.SmurfDetection; 

public class ExplanationStep 
{
    public int iteration { get; set; }
    public string identifierType { get; set; }
    public SmurfDetectionIdentifierGroup[] identifierGroups { get; set; }
    public string[] filteredIdentifiers { get; set; }
}