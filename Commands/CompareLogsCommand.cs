using System.Text.Json;
using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class CompareLogsCommand : BaseCommand
{
    public CompareLogsCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "compare-logs";
    public override string Description => "Analyze/compare JSON log files";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);

        string? json1 = null,
            json2 = null;
        foreach (var arg in args)
        {
            if (json1 == null)
                json1 = arg;
            else
                json2 = arg;
        }

        if (json1 == null)
            throw new ArgumentException("JSON file not specified");

        if (!File.Exists(json1))
            throw new FileNotFoundException($"File not found: {json1}");

        Logger.Header("JSON Analysis");

        if (json2 == null)
        {
            Logger.Info($"File: {json1}");
            Logger.Info("");
            AnalyzeSingleJson(json1);
        }
        else
        {
            if (!File.Exists(json2))
                throw new FileNotFoundException($"File not found: {json2}");

            Logger.Info($"JSON 1: {json1}");
            Logger.Info($"JSON 2: {json2}");
            Logger.Info("");
            CompareTwoJsons(json1, json2);
        }

        return 0;
    }

    private void AnalyzeSingleJson(string jsonPath)
    {
        try
        {
            using var stream = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("summary", out var summary))
            {
                if (summary.TryGetProperty("total_sorted_files", out var sorted))
                {
                    Logger.Info($"  Sorted files: {sorted}");
                    if (summary.TryGetProperty("total_source_files", out var source))
                        Logger.Info($"  Source files: {source}");
                    if (summary.TryGetProperty("total_duplicates", out var dups))
                        Logger.Info($"  Duplicates: {dups}");
                    if (summary.TryGetProperty("sorting_errors", out var errors))
                        Logger.Info($"  Sorting errors: {errors}");
                    if (summary.TryGetProperty("all_valid", out var valid))
                        Logger.Info($"  All valid: {valid}");
                }
                else
                {
                    if (summary.TryGetProperty("total_active_files", out var active))
                        Logger.Info($"  Active files: {active}");
                    if (summary.TryGetProperty("total_deleted_files", out var deleted))
                        Logger.Info($"  Deleted files: {deleted}");
                }
            }

            if (root.TryGetProperty("metadata", out var metadata))
            {
                Logger.Info("");
                if (metadata.TryGetProperty("version", out var version))
                    Logger.Info($"  Version: {version}");
                if (metadata.TryGetProperty("timestamp", out var timestamp))
                    Logger.Info($"  Timestamp: {timestamp}");
            }
        }
        catch (JsonException ex)
        {
            Logger.Error($"Invalid JSON: {ex.Message}");
        }
        Logger.Info("");
    }

    private void CompareTwoJsons(string json1Path, string json2Path)
    {
        HashSet<string> ExtractHashes(string path)
        {
            var set = new HashSet<string>();
            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("files", out var files))
                {
                    foreach (var file in files.EnumerateArray())
                    {
                        if (file.TryGetProperty("hash", out var h))
                        {
                            var val = h.GetString();
                            if (!string.IsNullOrEmpty(val))
                                set.Add(val);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse {path}: {ex.Message}");
            }
            return set;
        }

        var hashes1 = ExtractHashes(json1Path);
        var hashes2 = ExtractHashes(json2Path);

        var common = hashes1.Intersect(hashes2).Count();
        var only1 = hashes1.Except(hashes2).Count();
        var only2 = hashes2.Except(hashes1).Count();

        Logger.Info($"  JSON 1: {hashes1.Count} hashes");
        Logger.Info($"  JSON 2: {hashes2.Count} hashes");
        Logger.Info($"  Common: {common}");
        Logger.Info($"  Only in JSON 1: {only1}");
        Logger.Info($"  Only in JSON 2: {only2}");
        Logger.Info("");

        if (only1 == 0 && only2 == 0)
            Logger.Info("  [OK] Consistent!");
        else
            Logger.Info("  [!] Differences found");

        Logger.Info("");
    }
}
