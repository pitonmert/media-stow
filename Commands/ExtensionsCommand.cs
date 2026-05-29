using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class ExtensionsCommand : BaseCommand
{
    public ExtensionsCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "ext";
    public override string Description => "List file extensions in a directory";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);
        var targetDir = args.FirstOrDefault();
        FileService.RequireDirectory(targetDir, "Directory");

        Logger.Header("File Extensions");
        Logger.Info($"Directory: {targetDir}");
        Logger.Info("");

        var extensions = FileService
            .GetFilteredFiles(targetDir!)
            .Where(f => Path.HasExtension(f))
            .GroupBy(f => Path.GetExtension(f).ToLower().TrimStart('.'))
            .Select(g => new { Ext = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (extensions.Count == 0)
        {
            Logger.Info("  No files with extensions found");
            return 0;
        }

        foreach (var ext in extensions)
        {
            Logger.Info($"  {ext.Count, 4}  {ext.Ext}");
        }

        Logger.Info("");
        Logger.Info("  ----");
        Logger.Info($"  {extensions.Sum(x => x.Count)}  TOTAL");

        return 0;
    }
}
