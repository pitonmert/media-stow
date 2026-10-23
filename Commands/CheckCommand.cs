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
    public override string Description => "Screen media files for basic integrity problems";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);
        var targetDir = args.FirstOrDefault();
        FileService.RequireDirectory(targetDir, "Directory");

        Logger.Header("Basic Media Check");
        Logger.Info($"Directory: {targetDir}");
        Logger.Info("");

        var files = FileService
            .GetFilteredFiles(targetDir!, strict: true)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}logs{Path.DirectorySeparatorChar}")
            )
            .ToList();

        Logger.Info($"Total {files.Count} files to check...");
        Logger.Info("");

        var corrupt = new ConcurrentBag<string>();
        var unreadable = 0;
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
                            if (buffer.All(b => b == 0) || !HasValidSignature(ext, buffer))
                                isCorrupt = true;

                            var readBuffer = new byte[81920];
                            while (stream.Read(readBuffer) > 0) { }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        stats.AddOrUpdate("skipped", 1, (_, v) => v + 1);
                        Interlocked.Increment(ref unreadable);
                    }
                    catch (IOException ex) when ((ex.HResult & 0x0000FFFF) == 32)
                    {
                        stats.AddOrUpdate("skipped", 1, (_, v) => v + 1);
                        Interlocked.Increment(ref unreadable);
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

        if (corrupt.IsEmpty && unreadable == 0)
        {
            Logger.Info("  [OK] No problems detected by the basic check.");
        }
        else
        {
            Logger.Info($"  [!] {corrupt.Count} CORRUPT FILES:");
            foreach (var f in corrupt.OrderBy(x => x))
            {
                Logger.Info($"    - {Path.GetRelativePath(targetDir!, f)}");
            }
        }
        if (unreadable > 0)
            Logger.Info($"  [!] {unreadable} files could not be read");
        Logger.Info("");

        return corrupt.IsEmpty && unreadable == 0 ? 0 : 1;
    }

    private static bool HasValidSignature(string extension, byte[] header)
    {
        ReadOnlySpan<byte> bytes = header;
        return extension switch
        {
            "jpg" or "jpeg" => bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            "png" => bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            "bmp" => bytes.StartsWith("BM"u8),
            "webp" => bytes.StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes[8..].StartsWith("WEBP"u8),
            "tif" or "tiff" => bytes.StartsWith(new byte[] { 0x49, 0x49, 0x2A, 0x00 })
                || bytes.StartsWith(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }),
            "wav" => bytes.StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes[8..].StartsWith("WAVE"u8),
            "flac" => bytes.StartsWith("fLaC"u8),
            "ogg" => bytes.StartsWith("OggS"u8),
            "mp3" => bytes.StartsWith("ID3"u8)
                || bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0,
            _ => true,
        };
    }
}
