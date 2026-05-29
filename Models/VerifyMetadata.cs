using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class VerifyMetadata
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("sorted_dir")]
    public string SortedDir { get; set; } = "";

    [JsonPropertyName("source_dir")]
    public string SourceDir { get; set; } = "";
}
