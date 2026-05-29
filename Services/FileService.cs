using MediaStow.Abstractions;
using MediaStow.Configuration;
using MediaStow.Utils;

namespace MediaStow.Services;

public class FileService : IFileService
{
    private readonly ILogger _logger;

    public FileService(ILogger logger)
    {
        _logger = logger;
    }

    public bool ShouldFilter(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path);

        if (FilterConfiguration.FilterExtensions.Contains(ext))
            return true;
        if (
            FilterConfiguration.FilterPrefixes.Any(p =>
                name.StartsWith(p, StringComparison.OrdinalIgnoreCase)
            )
        )
            return true;
        if (FilterConfiguration.FilterFolders.Contains(name))
            return true;

        return false;
    }

    public IEnumerable<string> GetFilteredFiles(string directory)
    {
        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] files = Array.Empty<string>();
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Verbose($"Access denied: {dir} - {ex.Message}");
            }
            catch (DirectoryNotFoundException)
            {
                _logger.Verbose($"Directory not found: {dir}");
            }
            catch (Exception ex)
            {
                _logger.Verbose($"Error accessing {dir}: {ex.Message}");
            }

            foreach (var file in files)
            {
                if (!ShouldFilter(file))
                    yield return file;
            }

            try
            {
                foreach (var subdir in Directory.GetDirectories(dir))
                {
                    if (ShouldFilter(subdir))
                        continue;

                    try
                    {
                        var attrs = File.GetAttributes(subdir);
                        if ((attrs & FileAttributes.ReparsePoint) != 0)
                        {
                            _logger.Verbose($"Skipping symlink/junction: {subdir}");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Verbose($"Could not read attributes for {subdir}: {ex.Message}");
                    }

                    stack.Push(subdir);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
            catch { }
        }
    }

    public void RequireDirectory(string? path, string name)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException($"{name} not specified. Use 'media-stow help' for usage.");
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{name} not found: {path}");
    }

    public long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    public byte[]? GetFileChunk(string path, int offset, int length)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < offset + length)
                length = (int)Math.Max(0, stream.Length - offset);
            if (length <= 0)
                return null;

            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[length];
            stream.ReadExactly(buffer, 0, length);
            return buffer;
        }
        catch
        {
            return null;
        }
    }

    public bool TrySafeMove(string source, string destination)
    {
        try
        {
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(destination))
            {
                _logger.Verbose($"Destination exists, skipping: {destination}");
                return false;
            }

            File.Move(source, destination);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Move failed: {Path.GetFileName(source)} - {ex.Message}");
            return false;
        }
    }

    public bool TrySafeCopy(string source, string destination)
    {
        try
        {
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(destination))
            {
                _logger.Verbose($"Destination exists, skipping: {destination}");
                return false;
            }

            File.Copy(source, destination);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Copy failed: {Path.GetFileName(source)} - {ex.Message}");
            return false;
        }
    }

    public long GetAvailableDiskSpace(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);

            if (fullPath.StartsWith(@"\\") || fullPath.StartsWith("//"))
            {
                _logger.Verbose($"UNC path detected: {fullPath} - disk space check skipped");
                return -1;
            }

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
                return -1;

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                _logger.Verbose($"Drive not ready: {root}");
                return -1;
            }

            return drive.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.Verbose($"Disk space check failed: {ex.Message}");
            return -1;
        }
    }

    public bool CheckDiskSpace(string targetDir, long requiredBytes)
    {
        var available = GetAvailableDiskSpace(targetDir);

        if (available < 0)
        {
            _logger.Warning(
                $"Could not verify disk space for '{targetDir}'. Proceeding without space check."
            );
            return true;
        }

        if (available < requiredBytes)
        {
            _logger.Error(
                $"Insufficient disk space. Required: {ByteFormatter.Format(requiredBytes)}, Available: {ByteFormatter.Format(available)}"
            );
            return false;
        }
        return true;
    }
}
