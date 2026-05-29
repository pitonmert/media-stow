using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaStow.Abstractions;
using MediaStow.Configuration;
using MediaStow.Models;
using MediaStow.Utils;

namespace MediaStow.Commands;

public class VerifyCommand : BaseCommand
{
    public VerifyCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "verify";
    public override string Description => "Verify sorted media structure";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);

        string? sortedDir = null,
            sourceDir = null;
        foreach (var arg in args)
        {
            if (sortedDir == null)
                sortedDir = arg;
            else
                sourceDir = arg;
        }

        FileService.RequireDirectory(sortedDir, "Sorted directory");

        if (sourceDir == null)
            VerifyQuick(sortedDir!);
        else
            VerifyFull(sortedDir!, sourceDir);

        return 0;
    }

    private void VerifyQuick(string targetDir)
    {
        Logger.Header("Quick Verification");
        Logger.Info($"Directory: {targetDir}");
        Logger.Info("");

        int totalFiles = 0,
            namingErrors = 0,
            sortingErrors = 0,
            locationErrors = 0;

        try
        {
            foreach (var categoryDir in Directory.GetDirectories(targetDir))
            {
                var categoryName = Path.GetFileName(categoryDir);
                if (categoryName.Equals("logs", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var extDir in Directory.GetDirectories(categoryDir))
                {
                    var ext = Path.GetFileName(extDir).ToLower();
                    var extUpper = ext.ToUpper();

                    var expectedCategory = CategoryConfiguration.GetCategory(ext);
                    var expectedDisplay =
                        expectedCategory != null
                            ? CategoryConfiguration.GetCategoryDisplay(expectedCategory)
                            : null;

                    if (
                        expectedDisplay != null
                        && !expectedDisplay.Equals(categoryName, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        Logger.Info(
                            $"  [X] Wrong category: {ext} in {categoryName} (expected: {expectedDisplay})"
                        );
                        locationErrors++;
                    }

                    var pattern = $"{extUpper}*.{ext}";
                    var files = Directory
                        .GetFiles(extDir, pattern)
                        .OrderBy(f => f, NaturalStringComparer.Instance)
                        .ToList();

                    if (files.Count == 0)
                        continue;

                    Logger.Info($"  [{categoryName}/{ext}] {files.Count} files");

                    long prevSize = long.MaxValue;
                    int expectedRank = 1;

                    foreach (var file in files)
                    {
                        totalFiles++;
                        var fileName = Path.GetFileName(file);

                        var match = Regex.Match(
                            fileName,
                            $@"^{extUpper}(\d+)\.{ext}$",
                            RegexOptions.IgnoreCase
                        );
                        if (!match.Success)
                        {
                            Logger.Verbose($"    Naming error: {fileName}");
                            namingErrors++;
                            continue;
                        }

                        var actualRank = int.Parse(match.Groups[1].Value);
                        if (actualRank != expectedRank)
                        {
                            Logger.Verbose(
                                $"    Sequence error: {fileName} (expected {expectedRank})"
                            );
                            namingErrors++;
                        }

                        var size = FileService.GetFileSize(file);
                        if (size > prevSize)
                        {
                            Logger.Verbose($"    Sorting error: {fileName}");
                            sortingErrors++;
                        }

                        prevSize = size;
                        expectedRank++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Verification failed: {ex.Message}");
            return;
        }

        var totalErrors = namingErrors + sortingErrors + locationErrors;

        Logger.Info("");
        Logger.Info("==============================================");
        Logger.Info("RESULT:");

        if (totalErrors == 0)
        {
            Logger.Info("  [OK] Correctly organized!");
        }
        else
        {
            Logger.Info($"  [!] Errors found: {totalErrors}");
            Logger.Info($"      Naming: {namingErrors}");
            Logger.Info($"      Sorting: {sortingErrors}");
            Logger.Info($"      Location: {locationErrors}");
        }

        Logger.Info($"  Total files: {totalFiles}");
        Logger.Info("");
    }

    private void VerifyFull(string sortedDir, string sourceDir)
    {
        FileService.RequireDirectory(sourceDir, "Source directory");

        Logger.Header("Full Verification");
        Logger.Info($"Sorted: {sortedDir}");
        Logger.Info($"Source: {sourceDir}");
        Logger.Info("");

        var logDir = Path.Combine(sortedDir, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"verify_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        var sourceFiles = FileService.GetFilteredFiles(sourceDir).ToList();

        var sortedFiles = FileService
            .GetFilteredFiles(sortedDir)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}")
            )
            .ToList();

        Logger.Info($"Sorted: {sortedFiles.Count} files");
        Logger.Info($"Source: {sourceFiles.Count} files");
        Logger.Info("");

        Logger.Info("Computing source hashes...");
        var sourceHashes =
            new ConcurrentDictionary<string, ConcurrentBag<(string Path, long Size)>>();
        var processed = 0;
        var total = sourceFiles.Count;

        Parallel.ForEach(
            sourceFiles,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                var hash = HashService.TryComputeHash(file);
                if (!string.IsNullOrEmpty(hash))
                {
                    var size = FileService.GetFileSize(file);
                    var bag = sourceHashes.GetOrAdd(hash, _ => new ConcurrentBag<(string, long)>());
                    bag.Add((file, size));
                }

                var current = Interlocked.Increment(ref processed);
                if (current % 100 == 0 || current == total)
                    Logger.ShowProgress(current, total, "Source");
            }
        );

        Logger.Info("");

        var results = new ConcurrentBag<VerifyFileResult>();
        int totalDuplicates = 0,
            sortingErrors = 0,
            unmatched = 0;

        foreach (var category in new[] { "photos", "videos", "audio" })
        {
            var categoryDisplay = CategoryConfiguration.GetCategoryDisplay(category);
            var categoryDir = Path.Combine(sortedDir, categoryDisplay);
            if (!Directory.Exists(categoryDir))
                continue;

            Logger.Info($"[{categoryDisplay}]");

            foreach (var extDir in Directory.GetDirectories(categoryDir))
            {
                var ext = Path.GetFileName(extDir).ToLower();
                var extUpper = ext.ToUpper();
                var pattern = $"{extUpper}*.{ext}";

                var files = Directory
                    .GetFiles(extDir, pattern)
                    .OrderBy(f => f, NaturalStringComparer.Instance)
                    .ToList();

                if (files.Count == 0)
                    continue;

                long prevSize = long.MaxValue;
                int rank = 0;

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    if (
                        !Regex.IsMatch(
                            fileName,
                            $@"^{extUpper}\d+\.{ext}$",
                            RegexOptions.IgnoreCase
                        )
                    )
                        continue;

                    rank++;
                    var hash = HashService.ComputeHash(file);
                    var size = FileService.GetFileSize(file);

                    var rankValid = size <= prevSize;
                    if (!rankValid)
                        Interlocked.Increment(ref sortingErrors);
                    prevSize = size;

                    var originals = new List<OriginalFile>();
                    if (
                        !string.IsNullOrEmpty(hash)
                        && sourceHashes.TryGetValue(hash, out var matches)
                    )
                    {
                        foreach (var m in matches)
                        {
                            originals.Add(
                                new OriginalFile
                                {
                                    Path = Path.GetRelativePath(sourceDir, m.Path),
                                    Filename = Path.GetFileName(m.Path),
                                    SizeBytes = m.Size,
                                }
                            );
                        }
                        Interlocked.Add(ref totalDuplicates, matches.Count - 1);
                    }
                    else
                    {
                        Interlocked.Increment(ref unmatched);
                    }

                    results.Add(
                        new VerifyFileResult
                        {
                            Id = results.Count + 1,
                            SortedPath = Path.GetRelativePath(sortedDir, file),
                            Hash = hash,
                            SizeBytes = size,
                            SizeMb = Math.Round(size / (1024.0 * 1024), 2),
                            Rank = rank,
                            RankValid = rankValid,
                            Category = category,
                            Extension = ext,
                            Originals = originals,
                            DuplicateCount = originals.Count,
                        }
                    );

                    if (results.Count % 100 == 0)
                        Console.Write($"\r  Processed: {results.Count}");
                }

                Logger.ClearProgress();
                Logger.Info($"  {ext}: {rank} files");
            }
        }

        Logger.Info("");

        var allValid = sortingErrors == 0 && unmatched == 0;
        var resultList = results.OrderBy(r => r.SortedPath).ToList();

        for (int i = 0; i < resultList.Count; i++)
            resultList[i].Id = i + 1;

        var report = new VerifyReport
        {
            Metadata = new VerifyMetadata
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                SortedDir = sortedDir,
                SourceDir = sourceDir,
            },
            Summary = new VerifySummary
            {
                TotalSortedFiles = resultList.Count,
                TotalSourceFiles = sourceFiles.Count,
                TotalDuplicates = totalDuplicates,
                SortingErrors = sortingErrors,
                UnmatchedFiles = unmatched,
                AllValid = allValid,
            },
            Files = resultList,
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var tempFile = logFile + ".tmp";
        using (var stream = File.Create(tempFile))
        {
            JsonSerializer.Serialize(stream, report, jsonOptions);
        }
        File.Move(tempFile, logFile, overwrite: true);

        Logger.Info("==============================================");
        Logger.Info("RESULT:");

        if (allValid)
            Logger.Info("  [OK] Verification successful!");
        else
            Logger.Info("  [!] Issues found");

        Logger.Info($"  Sorted: {resultList.Count}");
        Logger.Info($"  Source: {sourceFiles.Count}");
        Logger.Info($"  Duplicates: {totalDuplicates}");
        Logger.Info($"  Sorting errors: {sortingErrors}");
        Logger.Info($"  Unmatched: {unmatched}");
        Logger.Info($"  Report: {logFile}");
        Logger.Info("");
    }
}
