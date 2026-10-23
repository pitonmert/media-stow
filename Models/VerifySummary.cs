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

    [JsonPropertyName("missing_source_files")]
    public int MissingSourceFiles { get; set; }

    [JsonPropertyName("ignored_files")]
    public int IgnoredFiles { get; set; }

    [JsonPropertyName("naming_errors")]
    public int NamingErrors { get; set; }

    [JsonPropertyName("location_errors")]
    public int LocationErrors { get; set; }

    [JsonPropertyName("hash_errors")]
    public int HashErrors { get; set; }

    [JsonPropertyName("all_valid")]
    public bool AllValid { get; set; }
}
