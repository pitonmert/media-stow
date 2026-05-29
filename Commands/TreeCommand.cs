using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class TreeCommand : BaseCommand
{
    public TreeCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "tree";
    public override string Description => "Show folder structure";

    public override int Execute(string[] args)
    {
        args = ParseArgs(args);
        var targetDir = args.FirstOrDefault();
        FileService.RequireDirectory(targetDir, "Directory");

        Logger.Header("Folder Structure");
        Logger.Info($"Directory: {targetDir}");
        Logger.Info("");

        PrintTreeIterative(targetDir!);

        return 0;
    }

    private void PrintTreeIterative(string rootDir)
    {
        var stack = new Stack<(string Path, string Indent, bool IsLast, bool IsRoot)>();
        stack.Push((rootDir, "", true, true));

        while (stack.Count > 0)
        {
            var (dir, indent, isLast, isRoot) = stack.Pop();
            var name = isRoot ? dir : Path.GetFileName(dir);

            if (isRoot)
            {
                Logger.Info(name);
            }
            else
            {
                var prefix = isLast ? "`-- " : "|-- ";
                Logger.Info($"{indent}{prefix}{name}");
            }

            try
            {
                var subdirs = Directory
                    .GetDirectories(dir)
                    .Where(d => !FileService.ShouldFilter(d))
                    .OrderByDescending(d => d)
                    .ToArray();

                for (int i = 0; i < subdirs.Length; i++)
                {
                    var subIsLast = i == 0;
                    var newIndent = isRoot ? "" : indent + (isLast ? "    " : "|   ");
                    stack.Push((subdirs[i], newIndent, subIsLast, false));
                }
            }
            catch { }
        }
    }
}
