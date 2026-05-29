using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class VerifySummary
{
    [JsonPropertyName("total_sorted_files")]
    public int TotalSortedFiles { get; set; }

    [JsonPropertyName("total_source_files")]
    public int TotalSourceFiles { get; set; }

    [JsonPropertyName("total_duplicates")]
    public int TotalDuplicates { get; set; }

    [JsonPropertyName("sorting_errors")]
    public int SortingErrors { get; set; }

    [JsonPropertyName("unmatched_files")]
    public int UnmatchedFiles { get; set; }

    [JsonPropertyName("all_valid")]
    public bool AllValid { get; set; }
}
