using System.Text.Json;
using System.Text.RegularExpressions;
using MediaStow.Abstractions;
using MediaStow.Configuration;
using MediaStow.Models;
using MediaStow.Utils;

namespace MediaStow.Commands;

public class SyncCommand : BaseCommand
{
    public SyncCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "sync";
    public override string Description => "Organize and synchronize media library";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);

        string? sourceDir = null,
            mediaDir = null;
        bool dryRun = false,
            autoYes = false,
            initMode = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-n" or "--dry-run":
                    dryRun = true;
                    break;
                case "-y" or "--yes":
                    autoYes = true;
                    break;
                case "--init":
                    initMode = true;
                    break;
                default:
                    if (sourceDir == null)
                        sourceDir = arg;
                    else
                        mediaDir = arg;
                    break;
            }
        }

        if (initMode)
        {
            FileService.RequireDirectory(sourceDir, "Source directory");
            mediaDir ??= Path.Combine(sourceDir!, "sorted");
            SyncInit(sourceDir!, mediaDir, dryRun);
        }
        else
        {
            FileService.RequireDirectory(sourceDir, "Media directory");
            SyncUpdate(sourceDir!, dryRun, autoYes);
        }

        return 0;
    }

    private void SyncInit(string sourceDir, string targetDir, bool dryRun)
    {
        Logger.Header("Initial Organization");
        if (dryRun)
            Logger.Info("PREVIEW MODE");
        Logger.Info($"Source: {sourceDir}");
        Logger.Info($"Target: {targetDir}");
        Logger.Info("");

        var allFiles = FileService
            .GetFilteredFiles(sourceDir)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}")
            )
            .ToList();

        long totalSourceBytes = allFiles.Sum(f => FileService.GetFileSize(f));
        Logger.Info($"Found {allFiles.Count} files ({ByteFormatter.Format(totalSourceBytes)})");
        Logger.Info("");

        if (!dryRun)
        {
            if (
                !FileService.CheckDiskSpace(
                    Path.GetDirectoryName(targetDir) ?? targetDir,
                    totalSourceBytes
                )
            )
                return;

            if (!UserInteraction.Confirm("Continue?"))
            {
                Logger.Info("Cancelled.");
                return;
            }
            Logger.Info("");
        }

        var logDir = Path.Combine(targetDir, "logs");
        var logFile = Path.Combine(logDir, "current.json");

        if (!dryRun)
            Directory.CreateDirectory(logDir);

        var fileEntries = new List<MediaFileEntry>();
        int totalFiles = 0;
        long totalBytes = 0;

        foreach (var category in new[] { "photos", "videos", "audio" })
        {
            var categoryDisplay = CategoryConfiguration.GetCategoryDisplay(category);
            var extensions = CategoryConfiguration.GetExtensionsForCategory(category);

            if (extensions.Length == 0)
                continue;

            Logger.Info($"[{categoryDisplay}]");

            foreach (var ext in extensions)
            {
                var files = allFiles
                    .Where(f =>
                        Path.GetExtension(f)
                            .TrimStart('.')
                            .Equals(ext, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(f => new { Path = f, Size = FileService.GetFileSize(f) })
                    .OrderByDescending(f => f.Size)
                    .ToList();

                if (files.Count == 0)
                    continue;

                var extUpper = ext.ToUpper();
                var targetSubDir = Path.Combine(targetDir, categoryDisplay, ext);

                if (!dryRun)
                    Directory.CreateDirectory(targetSubDir);

                int counter = 1;
                foreach (var file in files)
                {
                    var hash = HashService.ComputeHash(file.Path);
                    var newName = $"{extUpper}{counter}.{ext}";
                    var targetFile = Path.Combine(targetSubDir, newName);

                    fileEntries.Add(
                        new MediaFileEntry
                        {
                            Id = fileEntries.Count + 1,
                            SortedPath = $"{categoryDisplay}/{ext}/{newName}",
                            Hash = hash,
                            SizeBytes = file.Size,
                            SizeMb = Math.Round(file.Size / (1024.0 * 1024), 2),
                            Rank = counter,
                            RankValid = true,
                            Category = category,
                            Extension = ext,
                            AddedInVersion = 1,
                            Status = "active",
                        }
                    );

                    if (!dryRun)
                        FileService.TrySafeCopy(file.Path, targetFile);

                    counter++;
                    totalFiles++;
                    totalBytes += file.Size;
                }

                Logger.Info($"  {ext}: {files.Count} files");
            }
        }

        Logger.Info("");

        if (!dryRun)
        {
            var index = new MediaIndex
            {
                Metadata = new MediaMetadata
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    MediaDir = targetDir,
                    Version = 1,
                },
                Summary = new MediaSummary { TotalActiveFiles = totalFiles, TotalDeletedFiles = 0 },
                Files = fileEntries,
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var tempFile = logFile + ".tmp";
            using (var stream = File.Create(tempFile))
            {
                JsonSerializer.Serialize(stream, index, jsonOptions);
            }
            File.Move(tempFile, logFile, overwrite: true);
        }

        Logger.Info("==============================================");
        Logger.Info("SUMMARY:");
        Logger.Info($"  Files: {totalFiles}");
        Logger.Info($"  Size: {ByteFormatter.Format(totalBytes)}");
        if (!dryRun)
            Logger.Info($"  Index: {logFile}");
        Logger.Info("");
    }

    private void SyncUpdate(string mediaDir, bool dryRun, bool autoYes)
    {
        Logger.Header("Synchronization");
        if (dryRun)
            Logger.Info("PREVIEW MODE");
        Logger.Info($"Directory: {mediaDir}");
        Logger.Info("");

        var logsDir = Path.Combine(mediaDir, "logs");
        var changesDir = Path.Combine(logsDir, "archive", "changes");
        var currentJson = Path.Combine(logsDir, "current.json");

        if (!File.Exists(currentJson))
            throw new FileNotFoundException(
                "current.json not found. For initial setup use: media-stow sync --init <source> [target]"
            );

        if (!dryRun)
            Directory.CreateDirectory(changesDir);

        int currentVersion = 0;
        if (Directory.Exists(changesDir))
        {
            var lastChange = Directory
                .GetFiles(changesDir, "v*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => n.StartsWith("v") && int.TryParse(n.Substring(1), out _))
                .OrderByDescending(n => int.Parse(n.Substring(1)))
                .FirstOrDefault();

            if (lastChange != null)
                currentVersion = int.Parse(lastChange.Substring(1));
        }

        int newVersion = currentVersion + 1;
        Logger.Info($"Version: v{currentVersion} -> v{newVersion}");
        Logger.Info("");

        MediaIndex index;
        try
        {
            using var stream = File.OpenRead(currentJson);
            index = JsonSerializer.Deserialize<MediaIndex>(stream)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse current.json: {ex.Message}");
        }

        var activeEntries = index.Files.Where(f => f.Status == "active").ToList();
        foreach (var entry in activeEntries)
        {
            if (entry.Hash.Length == 32)
            {
                var fullPath = Path.Combine(mediaDir, entry.SortedPath);
                if (File.Exists(fullPath))
                {
                    var newHash = HashService.TryComputeHash(fullPath);
                    if (!string.IsNullOrEmpty(newHash))
                    {
                        entry.Hash = newHash;
                    }
                }
            }
        }

        var jsonFiles = activeEntries.ToLookup(f => f.Hash);

        Logger.Info($"Index: {jsonFiles.Count} active files");
        Logger.Info("Scanning disk...");

        var diskFiles = new Dictionary<string, List<string>>();
        var newFiles = new List<(string Path, string Hash)>();
        var wrongLocation = new List<(string Path, string ExpectedTarget)>();
        int totalDiskFiles = 0;

        foreach (var file in FileService.GetFilteredFiles(mediaDir))
        {
            var relPath = Path.GetRelativePath(mediaDir, file);
            if (relPath.StartsWith("logs"))
                continue;

            totalDiskFiles++;

            var ext = Path.GetExtension(file).TrimStart('.').ToLower();
            var category = CategoryConfiguration.GetCategory(ext);
            if (category == null)
                continue;

            var hash = HashService.ComputeHash(file);
            if (string.IsNullOrEmpty(hash))
                continue;

            if (!diskFiles.TryGetValue(hash, out var diskList))
            {
                diskList = new List<string>();
                diskFiles[hash] = diskList;
            }
            diskList.Add(file);

            var expectedDisplay = CategoryConfiguration.GetCategoryDisplay(category);
            var extUpper = ext.ToUpper();
            var fileName = Path.GetFileName(file);
            var pathParts = relPath.Split(Path.DirectorySeparatorChar);

            var currentCategory = pathParts.Length > 0 ? pathParts[0] : "";
            var extFolder = pathParts.Length > 1 ? pathParts[1] : "";

            var isCorrect =
                currentCategory.Equals(expectedDisplay, StringComparison.OrdinalIgnoreCase)
                && extFolder.Equals(ext, StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(fileName, $@"^{extUpper}\d+\.{ext}$", RegexOptions.IgnoreCase);

            if (!isCorrect)
                wrongLocation.Add((file, $"{expectedDisplay}/{ext}"));

            if (!jsonFiles.Contains(hash))
                newFiles.Add((file, hash));
        }

        var deletedHashes = jsonFiles.Select(g => g.Key).Except(diskFiles.Keys).ToList();

        Logger.Info($"Disk: {totalDiskFiles} files");
        Logger.Info("");
        Logger.Info("Changes detected:");
        Logger.Info($"  New files: {newFiles.Count}");
        Logger.Info($"  Wrong location: {wrongLocation.Count}");
        Logger.Info($"  Deleted: {deletedHashes.Count}");
        Logger.Info("");

        if (newFiles.Count == 0 && wrongLocation.Count == 0 && deletedHashes.Count == 0)
        {
            Logger.Info("[OK] Everything is synchronized.");
            return;
        }

        var filesToProcess = new List<(string Path, string Target)>();

        foreach (var (path, target) in wrongLocation)
        {
            if (dryRun || autoYes)
            {
                filesToProcess.Add((path, target));
            }
            else
            {
                Logger.Info(
                    $"  {Path.GetFileName(path)}: {Path.GetRelativePath(mediaDir, path)} -> {target}/"
                );
                if (UserInteraction.Confirm("  Move?"))
                    filesToProcess.Add((path, target));
            }
        }

        foreach (var (path, hash) in newFiles)
        {
            var ext = Path.GetExtension(path).TrimStart('.').ToLower();
            var category = CategoryConfiguration.GetCategory(ext);
            if (category == null)
                continue;

            var target = $"{CategoryConfiguration.GetCategoryDisplay(category)}/{ext}";
            if (!wrongLocation.Any(w => w.Path == path))
                filesToProcess.Add((path, target));
        }

        if (filesToProcess.Count == 0 && deletedHashes.Count == 0)
        {
            Logger.Info("[OK] No actions needed.");
            return;
        }

        if (!dryRun && !autoYes && filesToProcess.Count > 0)
        {
            Logger.Info("");
            if (!UserInteraction.Confirm($"Process {filesToProcess.Count} files?"))
            {
                Logger.Info("Cancelled.");
                return;
            }
        }

        Logger.Info("");
        Logger.Info("Processing...");

        var affectedExtensions = new HashSet<string>();

        if (!dryRun)
        {
            foreach (var (path, target) in filesToProcess)
            {
                var extDir = Path.Combine(
                    mediaDir,
                    target.Replace('/', Path.DirectorySeparatorChar)
                );
                affectedExtensions.Add(target);

                if (!path.StartsWith(extDir))
                {
                    Directory.CreateDirectory(extDir);
                    var tempName = $"__new_{Guid.NewGuid():N}_{Path.GetFileName(path)}";
                    try
                    {
                        File.Move(path, Path.Combine(extDir, tempName));
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to move: {Path.GetFileName(path)} - {ex.Message}");
                    }
                }
            }

            foreach (var target in affectedExtensions)
            {
                var extDir = Path.Combine(
                    mediaDir,
                    target.Replace('/', Path.DirectorySeparatorChar)
                );
                var ext = target.Split('/').Last();
                var extUpper = ext.ToUpper();

                Logger.Info($"  [{target}] reorganizing...");

                var files = Directory
                    .GetFiles(extDir)
                    .Select(f => new { Path = f, Size = FileService.GetFileSize(f) })
                    .OrderByDescending(f => f.Size)
                    .ToList();

                int rank = 1;
                var moves = new List<(string Original, string Temp, string Final)>();
                bool abort = false;

                for (int i = 0; i < files.Count; i++)
                {
                    var newName = $"{extUpper}{rank + i}.{ext}";
                    var finalPath = Path.Combine(extDir, newName);
                    var tempPath = Path.Combine(extDir, $"__temp_{i}__");
                    moves.Add((files[i].Path, tempPath, finalPath));
                }

                int tempSuccessCount = 0;
                foreach (var m in moves)
                {
                    try
                    {
                        File.Move(m.Original, m.Temp);
                        tempSuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to move {m.Original} to temp: {ex.Message}");
                        abort = true;
                        break;
                    }
                }

                int finalSuccessCount = 0;
                if (!abort)
                {
                    for (int i = 0; i < moves.Count; i++)
                    {
                        try
                        {
                            File.Move(moves[i].Temp, moves[i].Final);
                            finalSuccessCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(
                                $"Failed to move {moves[i].Temp} to {moves[i].Final}: {ex.Message}"
                            );
                            abort = true;
                            break;
                        }
                    }
                }

                if (abort)
                {
                    Logger.Warning($"Rolling back changes in {extDir} due to error...");
                    for (int i = 0; i < finalSuccessCount; i++)
                    {
                        try
                        {
                            File.Move(moves[i].Final, moves[i].Original);
                        }
                        catch { }
                    }
                    for (int i = finalSuccessCount; i < tempSuccessCount; i++)
                    {
                        try
                        {
                            File.Move(moves[i].Temp, moves[i].Original);
                        }
                        catch { }
                    }
                    continue;
                }

                Logger.Info($"    {files.Count} files");
            }

            Logger.Info("Updating index...");

            var changeJson = Path.Combine(changesDir, $"v{newVersion}.json");
            var changes = new
            {
                metadata = new
                {
                    version = newVersion,
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    based_on = $"v{currentVersion}",
                },
                changes = new
                {
                    added = newFiles.Count,
                    deleted = deletedHashes.Count,
                    moved = wrongLocation.Count,
                },
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var tempChangeFile = changeJson + ".tmp";
            using (var stream = File.Create(tempChangeFile))
            {
                JsonSerializer.Serialize(stream, changes, jsonOptions);
            }
            File.Move(tempChangeFile, changeJson, overwrite: true);

            var newEntries = new List<MediaFileEntry>();

            foreach (var entry in index.Files.Where(f => f.Status == "deleted"))
                newEntries.Add(entry);

            foreach (var hash in deletedHashes)
            {
                if (jsonFiles.Contains(hash))
                {
                    var old = jsonFiles[hash].First();
                    newEntries.Add(
                        new MediaFileEntry
                        {
                            Hash = hash,
                            SortedPath = old.SortedPath,
                            AddedInVersion = old.AddedInVersion,
                            Status = "deleted",
                            DeletedInVersion = newVersion,
                        }
                    );
                }
            }

            int totalActive = 0;
            foreach (var category in new[] { "photos", "videos", "audio" })
            {
                var categoryDisplay = CategoryConfiguration.GetCategoryDisplay(category);
                var categoryDir = Path.Combine(mediaDir, categoryDisplay);
                if (!Directory.Exists(categoryDir))
                    continue;

                foreach (var extDir in Directory.GetDirectories(categoryDir))
                {
                    var ext = Path.GetFileName(extDir).ToLower();
                    var extUpper = ext.ToUpper();
                    var pattern = $"{extUpper}*.{ext}";

                    var files = Directory
                        .GetFiles(extDir, pattern)
                        .OrderBy(f => f, NaturalStringComparer.Instance)
                        .ToList();

                    int rank = 1;
                    foreach (var file in files)
                    {
                        totalActive++;
                        var hash = HashService.ComputeHash(file);
                        var size = FileService.GetFileSize(file);

                        var addedVer = jsonFiles.Contains(hash)
                            ? jsonFiles[hash].First().AddedInVersion
                            : newVersion;

                        newEntries.Add(
                            new MediaFileEntry
                            {
                                Id = totalActive,
                                SortedPath = Path.GetRelativePath(mediaDir, file)
                                    .Replace(Path.DirectorySeparatorChar, '/'),
                                Hash = hash,
                                SizeBytes = size,
                                SizeMb = Math.Round(size / (1024.0 * 1024), 2),
                                Rank = rank,
                                RankValid = true,
                                Category = category,
                                Extension = ext,
                                AddedInVersion = addedVer,
                                Status = "active",
                            }
                        );
                        rank++;
                    }
                }
            }

            var newIndex = new MediaIndex
            {
                Metadata = new MediaMetadata
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    MediaDir = mediaDir,
                    SyncedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Version = newVersion,
                },
                Summary = new MediaSummary
                {
                    TotalActiveFiles = totalActive,
                    TotalDeletedFiles = newEntries.Count(e => e.Status == "deleted"),
                },
                Files = newEntries,
            };

            var currentTemp = currentJson + ".tmp";
            using (var currentStream = File.Create(currentTemp))
            {
                JsonSerializer.Serialize(currentStream, newIndex, jsonOptions);
            }
            File.Move(currentTemp, currentJson, overwrite: true);

            Logger.Info(
                $"  Version {newVersion}: {totalActive} active, {newIndex.Summary.TotalDeletedFiles} deleted"
            );
        }

        Logger.Info("");
        Logger.Info("==============================================");
        Logger.Info("SUMMARY:");
        Logger.Info($"  Added: {newFiles.Count}");
        Logger.Info($"  Moved: {wrongLocation.Count}");
        Logger.Info($"  Deleted: {deletedHashes.Count}");
        Logger.Info("[OK] Synchronization complete!");
        Logger.Info("");
    }
}
