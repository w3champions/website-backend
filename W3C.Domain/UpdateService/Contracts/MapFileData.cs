using System.Text.Json.Serialization;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.UpdateService.Contracts;

public class MapFileData
{
    public string Id { get; set; }
    public int MapId { get; set; }
    public string FilePath { get; set; }
    public GameMap MetaData { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 of utf8(mapProof), computed by update-service from the bytes it received
    /// and returned only to x-admin-secret callers. Present only for CustomGames/ rows. NEVER log it.
    /// Read from update-service (Newtonsoft) but never written by wb's own responses (System.Text.Json),
    /// so the admin map-file routes that return this type cannot re-serve it.
    /// </summary>
    [JsonIgnore]
    public string MapProofHash { get; set; }
}
