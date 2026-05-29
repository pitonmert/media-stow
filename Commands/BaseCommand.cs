using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public abstract class BaseCommand : ICommand
{
    protected readonly ILogger Logger;
    protected readonly IFileService FileService;
    protected readonly IHashService HashService;
    protected readonly IUserInteraction UserInteraction;
    protected readonly AppConfiguration Config;

    protected BaseCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
    {
        Logger = logger;
        FileService = fileService;
        HashService = hashService;
        UserInteraction = userInteraction;
        Config = config;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract int Execute(string[] args);

    protected string[] ParseArgs(string[] args)
    {
        Config.ParseGlobalOptions(ref args);
        return args;
    }
}
