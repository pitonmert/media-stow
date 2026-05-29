using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class MediaIndex
{
    [JsonPropertyName("metadata")]
    public MediaMetadata Metadata { get; set; } = new();

    [JsonPropertyName("summary")]
    public MediaSummary Summary { get; set; } = new();

    [JsonPropertyName("files")]
    public List<MediaFileEntry> Files { get; set; } = new();
}
