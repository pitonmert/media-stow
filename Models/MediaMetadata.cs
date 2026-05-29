using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class MediaMetadata
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("media_dir")]
    public string MediaDir { get; set; } = "";

    [JsonPropertyName("synced_at")]
    public string? SyncedAt { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }
}
