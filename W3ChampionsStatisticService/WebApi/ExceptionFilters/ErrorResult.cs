namespace W3ChampionsStatisticService.WebApi.ExceptionFilters;

public class ErrorResult(string error)
{
    public string Error { get; } = error;
}
