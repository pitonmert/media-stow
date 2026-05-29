using System.Collections.Concurrent;
using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class CompareCommand : BaseCommand
{
    public CompareCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "compare";
    public override string Description => "Compare two directories";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);

        string? dir1 = null,
            dir2 = null,
            extractDir = null;
        var mode = "name";

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--mode="))
                mode = arg.Substring(7);
            else if (arg == "--mode" && i + 1 < args.Length)
                mode = args[++i];
            else if (arg.StartsWith("--extract="))
                extractDir = arg.Substring(10);
            else if (arg == "--extract")
                extractDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[++i] : ".";
            else if (dir1 == null)
                dir1 = arg;
            else if (dir2 == null)
                dir2 = arg;
        }

        FileService.RequireDirectory(dir1, "First directory");
        FileService.RequireDirectory(dir2, "Second directory");

        if (mode == "hash")
            CompareByHash(dir1!, dir2!, extractDir);
        else
            CompareByName(dir1!, dir2!, extractDir);

        return 0;
    }

    private void CompareByName(string dir1, string dir2, string? extractDir)
    {
        Logger.Header("Folder Comparison");
        Logger.Info($"Directory 1: {dir1}");
        Logger.Info($"Directory 2: {dir2}");
        if (extractDir != null)
            Logger.Info($"Extract to: {extractDir}");
        Logger.Info("");

        HashSet<string?> folders1,
            folders2;
        try
        {
            folders1 = Directory.GetDirectories(dir1).Select(Path.GetFileName).ToHashSet();
            folders2 = Directory.GetDirectories(dir2).Select(Path.GetFileName).ToHashSet();
        }
        catch (Exception ex)
        {
            Logger.Error($"Cannot read directories: {ex.Message}");
            return;
        }

        var commonFolders = folders1
            .Intersect(folders2)
            .Where(f => f != null)
            .OrderBy(x => x)
            .ToList();

        if (commonFolders.Count == 0)
        {
            Logger.Info("  No common folders found");
            return;
        }

        int same = 0,
            different = 0,
            nameOnly = 0,
            extractCount = 0;

        for (int i = 0; i < commonFolders.Count; i++)
        {
            var folder = commonFolders[i]!;
            Logger.Info($"  [{i + 1}/{commonFolders.Count}] {folder}:");

            var path1 = Path.Combine(dir1, folder);
            var path2 = Path.Combine(dir2, folder);

            var files1 = FileService.GetFilteredFiles(path1).ToList();
            var files2 = FileService.GetFilteredFiles(path2).ToList();

            var hashes1 = new ConcurrentDictionary<string, string>();
            var hashes2 = new ConcurrentDictionary<string, string>();

            Parallel.ForEach(
                files1,
                f =>
                {
                    var h = HashService.TryComputeHash(f);
                    if (!string.IsNullOrEmpty(h))
                        hashes1.TryAdd(h, f);
                }
            );

            Parallel.ForEach(
                files2,
                f =>
                {
                    var h = HashService.TryComputeHash(f);
                    if (!string.IsNullOrEmpty(h))
                        hashes2.TryAdd(h, f);
                }
            );

            var set1 = hashes1.Keys.ToHashSet();
            var set2 = hashes2.Keys.ToHashSet();
            var onlyIn1 = set1.Except(set2).ToList();
            var onlyIn2 = set2.Except(set1).ToList();

            if (onlyIn1.Count == 0 && onlyIn2.Count == 0)
            {
                if (files1.Count == files2.Count)
                {
                    Logger.Info("    [OK] Content identical");
                    same++;
                }
                else
                {
                    Logger.Info("    [OK] Content identical (name differences only)");
                    nameOnly++;
                }
            }
            else
            {
                Logger.Info(
                    $"    [X] Content DIFFERENT (dir1: {onlyIn1.Count}, dir2: {onlyIn2.Count} unique)"
                );
                different++;

                if (extractDir != null)
                {
                    foreach (var hash in onlyIn1)
                    {
                        var src = hashes1[hash];
                        var dst = Path.Combine(
                            extractDir,
                            folder,
                            Path.GetFileName(dir1),
                            Path.GetFileName(src)
                        );
                        if (FileService.TrySafeMove(src, dst))
                            extractCount++;
                    }

                    foreach (var hash in onlyIn2)
                    {
                        var src = hashes2[hash];
                        var dst = Path.Combine(
                            extractDir,
                            folder,
                            Path.GetFileName(dir2),
                            Path.GetFileName(src)
                        );
                        if (FileService.TrySafeMove(src, dst))
                            extractCount++;
                    }
                }
            }
            Logger.Info("");
        }

        Logger.Info("  Summary:");
        Logger.Info($"    Common folders: {commonFolders.Count}");
        Logger.Info($"    Identical: {same}");
        Logger.Info($"    Name differences only: {nameOnly}");
        Logger.Info($"    Content different: {different}");
        if (extractDir != null && extractCount > 0)
            Logger.Info($"    Extracted: {extractCount} files");
    }

    private void CompareByHash(string dir1, string dir2, string? extractDir)
    {
        Logger.Header("Content Comparison (hash)");
        Logger.Info($"Directory 1: {dir1}");
        Logger.Info($"Directory 2: {dir2}");
        if (extractDir != null)
            Logger.Info($"Extract to: {extractDir}");
        Logger.Info("");

        var dir1Name = Path.GetFileName(dir1);
        var dir2Name = Path.GetFileName(dir2);

        var files1 = FileService.GetFilteredFiles(dir1).ToList();
        var files2 = FileService.GetFilteredFiles(dir2).ToList();

        Logger.Info($"[{dir1Name}] {files1.Count} files");
        Logger.Info($"[{dir2Name}] {files2.Count} files");
        Logger.Info("");

        Logger.Info($"Computing hashes for {dir1Name}...");
        var hashes1 = HashService.ComputeHashesParallel(files1, dir1Name);
        Logger.Info("");

        Logger.Info($"Computing hashes for {dir2Name}...");
        var hashes2 = HashService.ComputeHashesParallel(files2, dir2Name);
        Logger.Info("");

        var set1 = hashes1.Keys.ToHashSet();
        var set2 = hashes2.Keys.ToHashSet();

        var common = set1.Intersect(set2).Count();
        var only1 = set1.Except(set2).ToList();
        var only2 = set2.Except(set1).ToList();

        Logger.Info($"Unique hashes: [{dir1Name}] {set1.Count}, [{dir2Name}] {set2.Count}");
        Logger.Info("");
        Logger.Info("==============================================");
        Logger.Info("RESULT:");
        Logger.Info("");
        Logger.Info($"  Common: {common} unique files");

        int extractCount = 0;

        if (only1.Count > 0)
        {
            Logger.Info($"  Only in [{dir1Name}]: {only1.Count} files");
            if (extractDir != null)
            {
                foreach (var hash in only1)
                {
                    var src = hashes1[hash];
                    var rel = Path.GetRelativePath(dir1, src);
                    var dst = Path.Combine(extractDir, dir1Name, rel);
                    if (FileService.TrySafeMove(src, dst))
                        extractCount++;
                }
            }
        }

        if (only2.Count > 0)
        {
            Logger.Info($"  Only in [{dir2Name}]: {only2.Count} files");
            if (extractDir != null)
            {
                foreach (var hash in only2)
                {
                    var src = hashes2[hash];
                    var rel = Path.GetRelativePath(dir2, src);
                    var dst = Path.Combine(extractDir, dir2Name, rel);
                    if (FileService.TrySafeMove(src, dst))
                        extractCount++;
                }
            }
        }

        if (only1.Count == 0 && only2.Count == 0)
        {
            Logger.Info("");
            Logger.Info("  [OK] Both directories share identical content!");
        }

        if (extractDir != null && extractCount > 0)
            Logger.Info($"  Extracted: {extractCount} files -> {extractDir}");

        Logger.Info("");
    }
}
