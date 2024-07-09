namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

public class ErrorResult
{
    public string Error { get; }

    public ErrorResult(string error)
    {
        Error = error;
    }
}
