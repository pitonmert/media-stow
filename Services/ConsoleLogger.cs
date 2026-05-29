using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Services;

public class ConsoleLogger : ILogger
{
    private readonly AppConfiguration _config;
    private readonly object _consoleLock = new();

    public ConsoleLogger(AppConfiguration config)
    {
        _config = config;
    }

    public void Info(string message)
    {
        if (!_config.Quiet)
        {
            lock (_consoleLock)
            {
                Console.WriteLine(message);
            }
        }
    }

    public void Verbose(string message)
    {
        if (_config.Verbose)
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"[VERBOSE] {message}");
            }
        }
    }

    public void Error(string message)
    {
        lock (_consoleLock)
        {
            Console.Error.WriteLine($"Error: {message}");
        }
    }

    public void Warning(string message)
    {
        if (!_config.Quiet)
        {
            lock (_consoleLock)
            {
                Console.Error.WriteLine($"Warning: {message}");
            }
        }
    }

    public void Header(string title)
    {
        Info("");
        Info($"=== {title} ===");
        Info("");
    }

    public void ShowProgress(int current, int total, string prefix = "Processing")
    {
        if (_config.Quiet || total == 0)
            return;

        lock (_consoleLock)
        {
            var percent = current * 100 / total;
            var filled = current * 40 / total;
            var bar = new string('#', filled) + new string('-', 40 - filled);
            Console.Write($"\r{prefix}: [{bar}] {percent}% ({current}/{total})");
            if (current == total)
                Console.WriteLine();
        }
    }

    public void ClearProgress()
    {
        if (!_config.Quiet)
        {
            lock (_consoleLock)
            {
                Console.Write("\r" + new string(' ', 80) + "\r");
            }
        }
    }
}
