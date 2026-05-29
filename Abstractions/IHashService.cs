using System.Collections.Concurrent;

namespace MediaStow.Abstractions;

public interface IHashService
{
    string? TryComputeHash(string filePath);
    string ComputeHash(string filePath);
    ConcurrentDictionary<string, string> ComputeHashesParallel(List<string> files, string prefix);
}
