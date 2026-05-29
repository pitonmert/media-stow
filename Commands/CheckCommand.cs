using System.Collections.Concurrent;
using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class CheckCommand : BaseCommand
{
    public CheckCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "check";
    public override string Description => "Detect corrupt files";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);
        var targetDir = args.FirstOrDefault();
        FileService.RequireDirectory(targetDir, "Directory");

        Logger.Header("Corrupt File Check");
        Logger.Info($"Directory: {targetDir}");
        Logger.Info("");

        var files = FileService
            .GetFilteredFiles(targetDir!)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}")
            )
            .ToList();

        Logger.Info($"Total {files.Count} files to check...");
        Logger.Info("");

        var corrupt = new ConcurrentBag<string>();
        var stats = new ConcurrentDictionary<string, int>();
        stats["photos"] = 0;
        stats["videos"] = 0;
        stats["audio"] = 0;
        stats["skipped"] = 0;

        var processed = 0;
        var total = files.Count;

        Parallel.ForEach(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                var ext = Path.GetExtension(file).TrimStart('.').ToLower();
                var category = CategoryConfiguration.GetCategory(ext);

                if (category == null)
                {
                    stats.AddOrUpdate("skipped", 1, (_, v) => v + 1);
                }
                else
                {
                    stats.AddOrUpdate(category, 1, (_, v) => v + 1);

                    var isCorrupt = false;
                    try
                    {
                        using var stream = File.OpenRead(file);
                        if (stream.Length == 0)
                        {
                            isCorrupt = true;
                        }
                        else
                        {
                            var buffer = new byte[Math.Min(4096, stream.Length)];
                            stream.ReadExactly(buffer, 0, buffer.Length);
                            if (buffer.All(b => b == 0))
                                isCorrupt = true;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        stats.AddOrUpdate("skipped", 1, (_, v) => v + 1);
                    }
                    catch (IOException ex) when ((ex.HResult & 0x0000FFFF) == 32)
                    {
                        stats.AddOrUpdate("skipped", 1, (_, v) => v + 1);
                    }
                    catch
                    {
                        isCorrupt = true;
                    }

                    if (isCorrupt)
                    {
                        corrupt.Add(file);
                        Logger.Verbose($"CORRUPT: {Path.GetRelativePath(targetDir!, file)}");
                    }
                }

                var current = Interlocked.Increment(ref processed);
                if (current % 100 == 0 || current == total)
                    Logger.ShowProgress(current, total, "Checking");
            }
        );

        Logger.Info("");
        Logger.Info("==============================================");
        Logger.Info("RESULT:");
        Logger.Info(
            $"  Photos: {stats["photos"]}, Videos: {stats["videos"]}, Audio: {stats["audio"]}, Skipped: {stats["skipped"]}"
        );

        if (corrupt.IsEmpty)
        {
            Logger.Info("  [OK] No corrupt files found!");
        }
        else
        {
            Logger.Info($"  [!] {corrupt.Count} CORRUPT FILES:");
            foreach (var f in corrupt.OrderBy(x => x))
            {
                Logger.Info($"    - {Path.GetRelativePath(targetDir!, f)}");
            }
        }
        Logger.Info("");

        return 0;
    }
}
