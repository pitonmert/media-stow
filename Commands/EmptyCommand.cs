using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class EmptyCommand : BaseCommand
{
    public EmptyCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "empty";
    public override string Description => "Find empty folders";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);
        var targetDir = args.FirstOrDefault();
        FileService.RequireDirectory(targetDir, "Directory");

        Logger.Header("Empty Folders");
        Logger.Info($"Directory: {targetDir}");
        Logger.Info("");

        var emptyDirs = new List<string>();
        var stack = new Stack<string>();
        stack.Push(targetDir!);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                var subdirs = Directory.GetDirectories(dir);
                var files = Directory.GetFiles(dir);

                if (subdirs.Length == 0 && files.Length == 0)
                    emptyDirs.Add(dir);
                else
                    foreach (var subdir in subdirs)
                        stack.Push(subdir);
            }
            catch { }
        }

        if (emptyDirs.Count == 0)
        {
            Logger.Info("  No empty folders found");
        }
        else
        {
            foreach (var dir in emptyDirs.OrderBy(d => d))
            {
                Logger.Info($"  {Path.GetRelativePath(targetDir!, dir)}");
            }
            Logger.Info("");
            Logger.Info($"  Total: {emptyDirs.Count} empty folders");
        }

        return 0;
    }
}
