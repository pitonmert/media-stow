using System.Collections.Concurrent;
using System.Security.Cryptography;
using MediaStow.Abstractions;

namespace MediaStow.Services;

public class HashService : IHashService
{
    private readonly ILogger _logger;

    public HashService(ILogger logger)
    {
        _logger = logger;
    }

    public string? TryComputeHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLower();
        }
        catch (IOException ex)
        {
            _logger.Verbose($"Hash failed (IO): {filePath} - {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Verbose($"Hash failed (Access): {filePath} - {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Verbose($"Hash failed: {filePath} - {ex.Message}");
            return null;
        }
    }

    public string ComputeHash(string filePath)
    {
        return TryComputeHash(filePath) ?? string.Empty;
    }

    public ConcurrentDictionary<string, string> ComputeHashesParallel(
        List<string> files,
        string prefix
    )
    {
        var result = new ConcurrentDictionary<string, string>();
        var processed = 0;
        var total = files.Count;

        Parallel.ForEach(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file =>
            {
                var hash = TryComputeHash(file);
                if (!string.IsNullOrEmpty(hash))
                    result.TryAdd(hash, file);

                var current = Interlocked.Increment(ref processed);
                if (current % 50 == 0 || current == total)
                    _logger.ShowProgress(current, total, prefix);
            }
        );

        return result;
    }
}
