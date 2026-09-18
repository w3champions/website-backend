using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace W3C.Domain.MatchmakingService;

/// <summary>
/// Response of <c>GET /admin/getGamemodeParams/{id}</c>, which wraps the parameters
/// in a <c>params</c> property rather than returning them at the top level.
/// </summary>
public class GameModeParamsResponse
{
    [JsonProperty("params")]
    public GameModeParams Params { get; set; }
}

/// <summary>
/// Runtime-tunable parameters for a game mode.
/// <para>
/// Mirrors the matchmaking service's <c>params.schema.ts</c>, whose zod schemas are
/// the authority - it validates every write and rejects anything that does not match,
/// so this is not a guess at the shape. Only the modes that opt in have parameters
/// (currently Legion TD 2x2 and 4x4); both share this shape.
/// </para>
/// <para>
/// If a future mode needs different parameters, add its fields here as nullable rather
/// than widening the whole thing back to <c>object</c>: a caller can then see what is
/// available, and modes that do not use a field simply omit it.
/// </para>
/// </summary>
public class GameModeParams
{
    /// <summary>
    /// Windows during which the mode is promoted, or null when prime time is off.
    /// <para>
    /// The matchmaking service encodes "off" as the literal <c>false</c> rather than an
    /// empty array or null, hence <see cref="PrimeTimeConverter"/>.
    /// </para>
    /// </summary>
    [JsonProperty("primeTime")]
    [JsonConverter(typeof(PrimeTimeConverter))]
    public List<PrimeTimeWindow> PrimeTime { get; set; }

    [JsonProperty("matchmaking")]
    public MatchmakingParams Matchmaking { get; set; }
}

public class PrimeTimeWindow
{
    /// <summary>Hour of the day the window opens, 0-23.</summary>
    [JsonProperty("hour")]
    public int Hour { get; set; }

    /// <summary>Length of the window in minutes, 5-30.</summary>
    [JsonProperty("durationMinutes")]
    public int DurationMinutes { get; set; }
}

/// <summary>
/// Glicko-2 tuning for the mode. Named as the matchmaking service names them, single
/// letters and all, so the two sides stay greppable against each other.
/// </summary>
public class MatchmakingParams
{
    [JsonProperty("c")]
    public double C { get; set; }

    /// <summary>Volatility constraint, 0-1. Must be greater than <see cref="TauMax"/>.</summary>
    [JsonProperty("tau")]
    public double Tau { get; set; }

    [JsonProperty("C_tau")]
    public double? CTau { get; set; }

    /// <summary>Upper bound on volatility, 0-1.</summary>
    [JsonProperty("tau_max")]
    public double TauMax { get; set; }

    [JsonProperty("m")]
    public double? M { get; set; }
}

/// <summary>
/// Reads <c>primeTime</c>, which is either an array of windows or the literal
/// <c>false</c>. Maps <c>false</c> to null so callers have one "off" representation,
/// and writes null back as <c>false</c> so a round-trip through this client produces
/// a payload the service's validator still accepts.
/// </summary>
public class PrimeTimeConverter : JsonConverter<List<PrimeTimeWindow>>
{
    public override List<PrimeTimeWindow> ReadJson(
        JsonReader reader,
        Type objectType,
        List<PrimeTimeWindow> existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType is JsonToken.Boolean or JsonToken.Null)
        {
            reader.Skip();
            return null;
        }

        return serializer.Deserialize<List<PrimeTimeWindow>>(reader);
    }

    public override void WriteJson(JsonWriter writer, List<PrimeTimeWindow> value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteValue(false);
            return;
        }

        serializer.Serialize(writer, value);
    }
}
