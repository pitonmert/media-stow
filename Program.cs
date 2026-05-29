using MediaStow.Abstractions;
using MediaStow.Commands;
using MediaStow.Configuration;
using MediaStow.Services;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var commandName = args[0].ToLower();

        // Handle help aliases
        if (commandName is "-h" or "--help")
            commandName = "help";

        var services = ConfigureServices();
        var registry = CreateCommandRegistry(services);
        var command = registry.GetCommand(commandName);

        if (command == null)
        {
            var logger = services.GetRequiredService<ILogger>();
            logger.Error($"Unknown command: {commandName}");
            Console.WriteLine("Use 'media-stow help' for usage information.");
            return 1;
        }

        try
        {
            var cmdArgs = args.Skip(1).ToArray();
            return command.Execute(cmdArgs);
        }
        catch (Exception ex)
        {
            var config = services.GetRequiredService<AppConfiguration>();
            var logger = services.GetRequiredService<ILogger>();
            logger.Error(ex.Message);
            if (config.Verbose)
                Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Configuration
        services.AddSingleton<AppConfiguration>();

        // Core services
        services.AddSingleton<ILogger, ConsoleLogger>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IHashService, HashService>();
        services.AddSingleton<IUserInteraction, UserInteractionService>();

        // Commands
        services.AddTransient<ExtensionsCommand>();
        services.AddTransient<EmptyCommand>();
        services.AddTransient<TreeCommand>();
        services.AddTransient<CheckCommand>();
        services.AddTransient<CompareCommand>();
        services.AddTransient<ExtractCommonCommand>();
        services.AddTransient<CompareLogsCommand>();
        services.AddTransient<VerifyCommand>();
        services.AddTransient<DedupCommand>();
        services.AddTransient<SyncCommand>();
        services.AddTransient<HelpCommand>();

        return services.BuildServiceProvider();
    }

    static CommandRegistry CreateCommandRegistry(IServiceProvider services)
    {
        var registry = new CommandRegistry();

        registry.Register(services.GetRequiredService<ExtensionsCommand>());
        registry.Register(services.GetRequiredService<EmptyCommand>());
        registry.Register(services.GetRequiredService<TreeCommand>());
        registry.Register(services.GetRequiredService<CheckCommand>());
        registry.Register(services.GetRequiredService<CompareCommand>());
        registry.Register(services.GetRequiredService<ExtractCommonCommand>());
        registry.Register(services.GetRequiredService<CompareLogsCommand>());
        registry.Register(services.GetRequiredService<VerifyCommand>());
        registry.Register(services.GetRequiredService<DedupCommand>());
        registry.Register(services.GetRequiredService<SyncCommand>());
        registry.Register(services.GetRequiredService<HelpCommand>());

        return registry;
    }

    static void ShowHelp()
    {
        Console.WriteLine("MEDIA-STOW - Media file analysis and organization tool");
        Console.WriteLine("Usage: media-stow <command> [options] <directories>");
        Console.WriteLine("Use 'media-stow help' for detailed usage information.");
    }
}
