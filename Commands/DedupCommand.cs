using System.Collections.Concurrent;
using MediaStow.Abstractions;
using MediaStow.Configuration;
using MediaStow.Utils;

namespace MediaStow.Commands;

public class DedupCommand : BaseCommand
{
    public DedupCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "dedup";
    public override string Description => "Find and remove duplicate files";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);

        string? sourceDir = null,
            targetDir = null;
        bool dryRun = false;

        foreach (var arg in args)
        {
            if (arg is "-n" or "--dry-run")
                dryRun = true;
            else if (sourceDir == null)
                sourceDir = arg;
            else
                targetDir = arg;
        }

        FileService.RequireDirectory(sourceDir, "Source directory");
        targetDir ??= Path.Combine(sourceDir!, "duplicates");

        Logger.Header("Duplicate Cleanup");
        if (dryRun)
            Logger.Info("PREVIEW MODE");
        Logger.Info($"Source: {sourceDir}");
        Logger.Info($"Target: {targetDir}");
        Logger.Info("");

        var files = FileService.GetFilteredFiles(sourceDir!).ToList();
        Logger.Info($"Scanning {files.Count} files...");
        Logger.Info("");

        Logger.Info("Stage 1: Grouping by size...");
        var filesBySize = new Dictionary<long, List<string>>();
        foreach (var file in files)
        {
            var size = FileService.GetFileSize(file);
            if (size == 0)
                continue;

            if (!filesBySize.TryGetValue(size, out var list))
            {
                list = new List<string>();
                filesBySize[size] = list;
            }
            list.Add(file);
        }

        var potentialDuplicates = filesBySize
            .Where(kv => kv.Value.Count > 1)
            .SelectMany(kv => kv.Value)
            .ToList();

        Logger.Info(
            $"  {filesBySize.Count} unique sizes, {potentialDuplicates.Count} potential duplicates"
        );
        Logger.Info("");

        if (potentialDuplicates.Count == 0)
        {
            Logger.Info("No duplicates found (all files have unique sizes).");
            return 0;
        }

        Logger.Info("Stage 2: Comparing file content...");
        var filesByHash = new ConcurrentDictionary<string, ConcurrentBag<string>>();
        var processed = 0;
        var total = potentialDuplicates.Count;
        const int chunkSize = 4096;

        var sizeGroups = filesBySize.Where(kv => kv.Value.Count > 1).ToList();
        foreach (var sizeGroup in sizeGroups)
        {
            var filesInGroup = sizeGroup.Value;
            var size = sizeGroup.Key;

            if (size < chunkSize * 2)
            {
                Parallel.ForEach(
                    filesInGroup,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    file =>
                    {
                        var hash = HashService.TryComputeHash(file);
                        if (!string.IsNullOrEmpty(hash))
                        {
                            var bag = filesByHash.GetOrAdd(hash, _ => new ConcurrentBag<string>());
                            bag.Add(file);
                        }
                        Interlocked.Increment(ref processed);
                    }
                );
            }
            else
            {
                var chunkGroups = new Dictionary<string, List<string>>();
                foreach (var file in filesInGroup)
                {
                    var chunk = FileService.GetFileChunk(file, 0, chunkSize);
                    if (chunk == null)
                        continue;

                    var chunkKey = Convert.ToHexString(chunk);
                    if (!chunkGroups.TryGetValue(chunkKey, out var list))
                    {
                        list = new List<string>();
                        chunkGroups[chunkKey] = list;
                    }
                    list.Add(file);
                }

                foreach (var chunkGroup in chunkGroups.Where(cg => cg.Value.Count > 1))
                {
                    Parallel.ForEach(
                        chunkGroup.Value,
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        file =>
                        {
                            var hash = HashService.TryComputeHash(file);
                            if (!string.IsNullOrEmpty(hash))
                            {
                                var bag = filesByHash.GetOrAdd(
                                    hash,
                                    _ => new ConcurrentBag<string>()
                                );
                                bag.Add(file);
                            }
                            Interlocked.Increment(ref processed);
                        }
                    );
                }

                processed += chunkGroups.Where(cg => cg.Value.Count == 1).Sum(cg => cg.Value.Count);
            }

            if (processed % 50 == 0 || processed == total)
                Logger.ShowProgress(processed, total, "Comparing");
        }

        Logger.Info("");

        var duplicateGroups = filesByHash.Where(kv => kv.Value.Count > 1).ToList();

        if (duplicateGroups.Count == 0)
        {
            Logger.Info("No duplicates found.");
            return 0;
        }

        long totalSavedBytes = 0;
        foreach (var group in duplicateGroups)
        {
            var list = group.Value.ToList();
            for (int i = 1; i < list.Count; i++)
                totalSavedBytes += FileService.GetFileSize(list[i]);
        }

        Logger.Info($"Found {duplicateGroups.Count} duplicate groups.");
        Logger.Info($"Potential savings: {ByteFormatter.Format(totalSavedBytes)}");
        Logger.Info("");

        if (!dryRun)
        {
            if (!UserInteraction.Confirm("Continue?"))
            {
                Logger.Info("Cancelled.");
                return 0;
            }
            Logger.Info("");
        }

        int groups = 0,
            moved = 0,
            deleted = 0;
        long savedBytes = 0;

        var logDir = Path.Combine(sourceDir!, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"dedup_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var logLines = new List<string>();

        foreach (var group in duplicateGroups)
        {
            groups++;
            var list = group.Value.OrderByDescending(f => FileService.GetFileSize(f)).ToList();
            var hash = group.Key.Substring(0, 8);
            var firstFile = list[0];
            var fileName = Path.GetFileName(firstFile);

            logLines.Add($"[{groups}] {fileName} (hash: {hash})");
            logLines.Add($"  Keep: {firstFile}");

            for (int i = 1; i < list.Count; i++)
            {
                var dup = list[i];
                var dupSize = FileService.GetFileSize(dup);
                savedBytes += dupSize;

                logLines.Add($"  Delete: {dup} ({ByteFormatter.Format(dupSize)})");

                if (!dryRun)
                {
                    try
                    {
                        File.Delete(dup);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to delete: {dup} - {ex.Message}");
                    }
                }
            }

            if (!dryRun)
            {
                var targetFolder = Path.Combine(targetDir!, $"{fileName}_{hash}");
                try
                {
                    Directory.CreateDirectory(targetFolder);
                    var targetPath = Path.Combine(targetFolder, fileName);
                    File.Move(firstFile, targetPath);
                    moved++;

                    var sourcesFile = Path.Combine(targetFolder, "_sources.txt");
                    File.WriteAllLines(sourcesFile, list);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to move: {fileName} - {ex.Message}");
                }
            }

            if (groups % 10 == 0)
                Logger.ShowProgress(groups, duplicateGroups.Count);
        }

        Logger.ShowProgress(duplicateGroups.Count, duplicateGroups.Count, "Completed");
        Logger.Info("");

        if (!dryRun)
            File.WriteAllLines(logFile, logLines);

        Logger.Info("==============================================");
        Logger.Info("SUMMARY:");
        Logger.Info($"  Groups: {groups}");
        Logger.Info($"  Moved: {moved}");
        Logger.Info($"  Deleted: {deleted}");
        Logger.Info($"  Saved: {ByteFormatter.Format(savedBytes)}");
        if (!dryRun)
            Logger.Info($"  Log: {logFile}");
        Logger.Info("");

        return 0;
    }
}
