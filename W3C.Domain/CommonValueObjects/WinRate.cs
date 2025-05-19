namespace W3C.Domain.CommonValueObjects;

public class WinRate(in int wins, in int losses)
{
    public double Rate { get; set; } = losses + wins != 0 ? wins / (double)(wins + losses) : 0;
}
