namespace W3ChampionsStatisticService.Hubs;

public class WebsiteBackendSocketResponseType
{
    private WebsiteBackendSocketResponseType(string value) { Value = value; }

    public string Value { get; private set; }

    public static WebsiteBackendSocketResponseType Connected { get { return new WebsiteBackendSocketResponseType("Connected"); } }

    public override string ToString()
    {
        return Value;
    }
}
