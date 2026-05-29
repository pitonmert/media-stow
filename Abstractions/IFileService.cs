namespace MediaStow.Abstractions;

public interface IFileService
{
    IEnumerable<string> GetFilteredFiles(string directory);
    bool ShouldFilter(string path);
    bool TrySafeMove(string source, string destination);
    bool TrySafeCopy(string source, string destination);
    long GetFileSize(string path);
    byte[]? GetFileChunk(string path, int offset, int length);
    long GetAvailableDiskSpace(string path);
    void RequireDirectory(string? path, string name);
    bool CheckDiskSpace(string targetDir, long requiredBytes);
}
