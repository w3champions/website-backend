using System.Collections.Generic;
using W3ChampionsStatisticService.Admin.SmurfDetection;

public class PossibleIdentifierTypesResponse
{
    public List<string> possibleIdentifierTypes { get; set; }
}


public class RebuildSmurfDatabaseResponse
{
    public string message { get; set; }
}


public class GetIgnoredIdentifierResponse
{
    public IgnoredIdentifier ignoredIdentifier { get; set; }
}


public class GetIgnoredIdentifiersResponse
{
    public List<IgnoredIdentifier> identifiers { get; set; }
    public string continuationToken { get; set; }
}


public class AddIgnoredIdentifierResponse
{
    public string message { get; set; }
    public IgnoredIdentifier newIdentifier { get; set; }
}