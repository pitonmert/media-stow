using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class ExtractCommonCommand : BaseCommand
{
    public ExtractCommonCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "extract-common";
    public override string Description => "Extract common files between directories";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);

        if (args.Length < 3)
            throw new ArgumentException(
                "Usage: extract-common <icloudDir> <mediaDir> <targetBase>"
            );

        string dir1 = args[0],
            dir2 = args[1],
            targetBase = args[2];
        FileService.RequireDirectory(dir1, "iCloud directory");
        FileService.RequireDirectory(dir2, "Media directory");

        Logger.Header("Extracting Common Files");
        Logger.Info($"iCloud (Source - Move): {dir1}");
        Logger.Info($"Media (Source - Copy): {dir2}");
        Logger.Info($"Target Base: {targetBase}");
        Logger.Info("");

        var files1 = FileService.GetFilteredFiles(dir1).ToList();
        var files2 = FileService.GetFilteredFiles(dir2).ToList();

        Logger.Info("Scanning files...");
        var hashes1 = HashService.ComputeHashesParallel(files1, "iCloud");
        var hashes2 = HashService.ComputeHashesParallel(files2, "Media");

        var commonHashes = hashes1.Keys.Intersect(hashes2.Keys).ToList();
        Logger.Info($"\nFound {commonHashes.Count} common files.");

        if (commonHashes.Count == 0)
        {
            Logger.Info("No common files found. Nothing to do.");
            return 0;
        }

        if (!UserInteraction.Confirm("Continue?"))
            return 0;

        string target1 = Path.Combine(targetBase, "iCloudFoto");
        string target2 = Path.Combine(targetBase, "media");
        Directory.CreateDirectory(target1);
        Directory.CreateDirectory(target2);

        int moved = 0,
            copied = 0;
        foreach (var hash in commonHashes)
        {
            var src1 = hashes1[hash];
            var src2 = hashes2[hash];

            var name1 = Path.GetFileName(src1);
            var dest1 = Path.Combine(target1, name1);
            if (File.Exists(dest1))
                dest1 = Path.Combine(
                    target1,
                    $"{Path.GetFileNameWithoutExtension(name1)}_{hash}{Path.GetExtension(name1)}"
                );
            if (FileService.TrySafeMove(src1, dest1))
                moved++;

            var name2 = Path.GetFileName(src2);
            var dest2 = Path.Combine(target2, name2);
            if (File.Exists(dest2))
                dest2 = Path.Combine(
                    target2,
                    $"{Path.GetFileNameWithoutExtension(name2)}_{hash}{Path.GetExtension(name2)}"
                );
            if (FileService.TrySafeCopy(src2, dest2))
                copied++;
        }

        Logger.Info("");
        Logger.Info("==============================================");
        Logger.Info("SUMMARY:");
        Logger.Info($"  Moved from iCloud: {moved}");
        Logger.Info($"  Copied from Media: {copied}");
        Logger.Info("[OK] Common files extraction complete!");

        return 0;
    }
}
