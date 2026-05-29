using System.Text.Json.Serialization;

namespace MediaStow.Models;

public class VerifyFileResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("sorted_path")]
    public string SortedPath { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("size_mb")]
    public double SizeMb { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("rank_valid")]
    public bool RankValid { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = "";

    [JsonPropertyName("originals")]
    public List<OriginalFile> Originals { get; set; } = new();

    [JsonPropertyName("duplicate_count")]
    public int DuplicateCount { get; set; }
}
