using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class MediaSummary
{
    [JsonPropertyName("total_active_files")]
    public int TotalActiveFiles { get; set; }

    [JsonPropertyName("total_deleted_files")]
    public int TotalDeletedFiles { get; set; }
}
