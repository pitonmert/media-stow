using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class VerifyReport
{
    [JsonPropertyName("metadata")]
    public VerifyMetadata Metadata { get; set; } = new();

    [JsonPropertyName("summary")]
    public VerifySummary Summary { get; set; } = new();

    [JsonPropertyName("files")]
    public List<VerifyFileResult> Files { get; set; } = new();
}
