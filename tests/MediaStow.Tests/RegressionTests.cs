using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using MediaStow.Abstractions;
using MediaStow.Commands;
using MediaStow.Configuration;
using MediaStow.Models;
using MediaStow.Services;
using Xunit;

namespace MediaStow.Tests;

public sealed class RegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DedupDoesNotTreatFileSymlinksAsIndependentCopies(bool relative)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var target = temp.Path("duplicates");
        var original = temp.File("source/original.mov", "placeholder");
        var link = temp.Path("source/link.mov");
        var linkTarget = relative ? "original.mov" : original;
        // Equal sizes also exercise the size prefilter used before hashing.
        System.IO.File.WriteAllText(original, new string('x', System.Text.Encoding.UTF8.GetByteCount(linkTarget)));
        System.IO.File.CreateSymbolicLink(link, linkTarget);
        var expectedHash = Sha256(original);

        var (command, logger, _) = Create<DedupCommand>(confirm: true);
        Assert.Equal(0, command.Execute([source, target]));

        Assert.Contains(logger.Messages, message => message.Contains("Scanning 1 files"));
        Assert.Equal(expectedHash, Sha256(original));
        Assert.Equal(expectedHash, Sha256(link));
        Assert.Equal(linkTarget, new FileInfo(link).LinkTarget);
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileScanSkipsFileSymlinksIncludingBrokenLinks(bool strict)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var original = temp.File("source/original.jpg", "original");
        System.IO.File.CreateSymbolicLink(temp.Path("source/link.jpg"), original);
        System.IO.File.CreateSymbolicLink(temp.Path("source/missing.jpg"), temp.Path("missing.jpg"));

        var files = new FileService(new TestLogger()).GetFilteredFiles(source, strict).ToList();

        Assert.Equal(new[] { original }, files);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void VerifySkipsFilteredFoldersAndSymlinksWithoutHidingInvalidFolders(bool full, bool invalidFolder)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");
        var original = temp.File("sorted/Photos/jpg/JPG1.jpg", "original");
        var ignored = temp.File("sorted/Photos/.sync-cache/nested/other.jpg", "cached data");
        temp.File("sorted/.fseventsd/jpg/other.jpg", "system data");
        System.IO.File.CreateSymbolicLink(temp.Path("sorted/Photos/jpg/JPG2.jpg"), original);
        Directory.CreateSymbolicLink(temp.Path("sorted/Photos/linked"), source);
        Directory.CreateSymbolicLink(temp.Path("sorted/LinkedPhotos"), temp.Path("sorted/Photos"));
        if (invalidFolder)
            temp.Directory("sorted/Photos/unrecognized");

        var (command, logger, _) = Create<VerifyCommand>();
        Assert.Equal(invalidFolder ? 1 : 0, command.Execute(full ? [sorted, source] : [sorted]));
        Assert.Equal("cached data", System.IO.File.ReadAllText(ignored));
        if (invalidFolder)
            Assert.DoesNotContain(logger.Messages, message => message.Contains("[OK]"));
        else
            Assert.Contains(logger.Messages, message => message.Contains("[OK]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyncRecordsTheChangedDuplicatePathAsDeleted(bool moveRemainingCopy)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", new string('A', 20));
        temp.File("source/b.jpg", new string('A', 20));
        temp.File("source/c.jpg", new string('B', 10));
        var (init, _, _) = Create<SyncCommand>(confirm: true);
        Assert.Equal(0, init.Execute(["--init", source, sorted]));
        var jpgDir = Path.Combine(sorted, "Photos", "jpg");
        var oldHash = Sha256(Path.Combine(jpgDir, "JPG2.jpg"));
        System.IO.File.WriteAllText(Path.Combine(jpgDir, "JPG2.jpg"), new string('B', 10));
        if (moveRemainingCopy)
            System.IO.File.Move(Path.Combine(jpgDir, "JPG1.jpg"), Path.Combine(jpgDir, "JPG4.jpg"));

        var (update, _, _) = Create<SyncCommand>();
        Assert.Equal(0, update.Execute(["-y", sorted]));

        var index = JsonSerializer.Deserialize<MediaIndex>(
            System.IO.File.ReadAllText(Path.Combine(sorted, "logs", "current.json")))!;
        var deleted = Assert.Single(index.Files, entry => entry.Status == "deleted");
        Assert.Equal("Photos/jpg/JPG2.jpg", deleted.SortedPath);
        Assert.Equal(oldHash, deleted.Hash);
        Assert.Equal(3, index.Summary.TotalActiveFiles);
        Assert.Equal(1, index.Summary.TotalDeletedFiles);
        foreach (var entry in index.Files.Where(entry => entry.Status == "active"))
            Assert.Equal(entry.Hash, Sha256(Path.Combine(sorted, entry.SortedPath)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void VerifyIgnoresFilteredFilesWithoutHidingMisplacedFiles(bool full, bool misplaced)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");
        temp.File("sorted/Photos/jpg/JPG1.jpg", "original");
        var helper = temp.File("sorted/Photos/jpg/._JPG1.jpg", "AppleDouble metadata");
        var sidecar = temp.File("sorted/Photos/jpg/JPG1.aae", "editing metadata");
        if (misplaced)
            temp.File("sorted/loose.jpg", "unexpected image");

        var (command, logger, _) = Create<VerifyCommand>();
        var exitCode = command.Execute(full ? [sorted, source] : [sorted]);

        Assert.Equal(misplaced ? 1 : 0, exitCode);
        Assert.True(System.IO.File.Exists(helper));
        Assert.True(System.IO.File.Exists(sidecar));
        if (misplaced)
            Assert.DoesNotContain(logger.Messages, message => message.Contains("[OK]"));
        else
            Assert.Contains(logger.Messages, message => message.Contains("[OK]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyncRestoresNestedIndexedFileAndPreservesEveryIndexEntry(bool sameHash)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "longer original content");
        temp.File("source/b.jpg", sameHash ? "longer original content" : "short");
        var (init, _, _) = Create<SyncCommand>(confirm: true);
        Assert.Equal(0, init.Execute(["--init", source, sorted]));
        var jpgDir = Path.Combine(sorted, "Photos", "jpg");
        var nestedDir = temp.Directory("sorted/Photos/jpg/nested");
        var nestedFile = Path.Combine(nestedDir, "JPG1.jpg");
        System.IO.File.Move(Path.Combine(jpgDir, "JPG1.jpg"), nestedFile);

        var (update, _, _) = Create<SyncCommand>();
        Assert.Equal(0, update.Execute(["-y", sorted]));

        var index = JsonSerializer.Deserialize<MediaIndex>(
            System.IO.File.ReadAllText(Path.Combine(sorted, "logs", "current.json")))!;
        Assert.Equal(2, index.Summary.TotalActiveFiles);
        Assert.Equal(0, index.Summary.TotalDeletedFiles);
        Assert.Equal(2, index.Files.Count);
        Assert.False(System.IO.File.Exists(nestedFile));
        Assert.Equal(2, Directory.GetFiles(jpgDir, "*.jpg").Length);
        Assert.Equal(
            new[] { Sha256(temp.Path("source/a.jpg")), Sha256(temp.Path("source/b.jpg")) }.Order(),
            index.Files.Select(entry => entry.Hash).Order());
        foreach (var entry in index.Files)
        {
            Assert.Equal("active", entry.Status);
            Assert.Equal(entry.Hash, Sha256(Path.Combine(sorted, entry.SortedPath)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DedupScansSourceWhenTargetIsItsParent(bool dryRun)
    {
        using var temp = new TemporaryDirectory();
        var target = temp.Directory("target");
        var source = temp.Directory("target/source");
        var first = temp.File("target/source/a.jpg", "same contents");
        temp.File("target/source/b.jpg", "same contents");
        var unrelated = temp.File("target/outside.jpg", "same contents");
        var expectedHash = Sha256(first);

        var (command, logger, _) = Create<DedupCommand>(confirm: true);
        var exitCode = command.Execute(dryRun ? ["-n", source, target] : [source, target]);

        Assert.Equal(0, exitCode);
        Assert.Contains(logger.Messages, message => message.Contains("Scanning 2 files"));
        Assert.Contains(logger.Messages, message => message.Contains("Groups: 1"));
        Assert.Equal(expectedHash, Sha256(unrelated));
        Assert.Equal(dryRun ? 2 : 0, Directory.GetFiles(source, "*.jpg").Length);
        var preserved = Directory.GetFiles(target, "*.jpg", SearchOption.AllDirectories);
        Assert.Equal(dryRun ? 3 : 2, preserved.Length);
        Assert.All(preserved, file => Assert.Equal(expectedHash, Sha256(file)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DedupExcludesExistingFilesInsideNestedTarget(bool dryRun)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var target = temp.Directory("source/duplicates");
        temp.File("source/a.jpg", "same contents");
        temp.File("source/b.jpg", "same contents");
        var existing = temp.File("source/duplicates/existing.jpg", "same contents");

        var (command, logger, _) = Create<DedupCommand>(confirm: true);
        var exitCode = command.Execute(dryRun ? ["-n", source, target] : [source, target]);

        Assert.Equal(0, exitCode);
        Assert.Contains(logger.Messages, message => message.Contains("Scanning 2 files"));
        Assert.Contains(logger.Messages, message => message.Contains("Groups: 1"));
        Assert.Equal("same contents", System.IO.File.ReadAllText(existing));
        Assert.Equal(dryRun ? 2 : 0, Directory.GetFiles(source, "*.jpg").Length);
        Assert.Equal(dryRun ? 1 : 2, Directory.GetFiles(target, "*.jpg", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void FullVerifySucceedsWhenEverySourceFileIsPresent()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "first");
        temp.File("sorted/Photos/jpg/JPG1.jpg", "first");

        var (command, logger, _) = Create<VerifyCommand>();
        var exitCode = command.Execute([sorted, source]);

        Assert.Equal(0, exitCode);
        Assert.Contains(logger.Messages, m => m.Contains("Verification successful!"));
    }

    [Fact]
    public void InitialSyncCopiesAndIndexesAFileWhenTargetIsEmpty()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");

        var (command, _, _) = Create<SyncCommand>(confirm: true);
        command.Execute(["--init", source, sorted]);

        var indexPath = Path.Combine(sorted, "logs", "current.json");
        var index = JsonSerializer.Deserialize<MediaIndex>(System.IO.File.ReadAllText(indexPath))!;
        var entry = Assert.Single(index.Files);
        Assert.Equal(entry.Hash, Sha256(Path.Combine(sorted, entry.SortedPath)));
    }

    [Fact]
    public void FullVerifyFailsWhenSourceFileIsMissingFromSortedLibrary()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "first");
        temp.File("source/b.jpg", "missing");
        temp.File("sorted/Photos/jpg/JPG1.jpg", "first");

        var (command, logger, _) = Create<VerifyCommand>();
        var exitCode = command.Execute([sorted, source]);

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Verification successful!"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifyFailsForUnexpectedFileInExtensionFolder(bool full)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "first");
        temp.File("sorted/Photos/jpg/JPG1.jpg", "first");
        temp.File("sorted/Photos/jpg/untracked.jpg", "unexpected");

        var (command, logger, _) = Create<VerifyCommand>();
        var exitCode = command.Execute(full ? [sorted, source] : [sorted]);

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("[OK]"));
    }

    [Fact]
    public void QuickVerifyReturnsFailureExitCodeForInvalidRankOrder()
    {
        using var temp = new TemporaryDirectory();
        var sorted = temp.Directory("sorted");
        temp.File("sorted/Photos/jpg/JPG1.jpg", "small");
        temp.File("sorted/Photos/jpg/JPG2.jpg", "larger file");

        var (command, logger, _) = Create<VerifyCommand>();
        var exitCode = command.Execute([sorted]);

        Assert.Contains(logger.Messages, m => m.Contains("Sorting: 1"));
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void FullVerifyFailsForMissingRankNumber()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "first");
        temp.File("sorted/Photos/jpg/JPG2.jpg", "first");

        var (command, logger, _) = Create<VerifyCommand>();
        var exitCode = command.Execute([sorted, source]);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(logger.Messages, m => m.Contains("Naming errors: 1"));
    }

    [Fact]
    public void InitialSyncNeverWritesAnIndexEntryWithWrongHash()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");
        temp.File("sorted/Photos/jpg/JPG1.jpg", "different");

        var (command, _, _) = Create<SyncCommand>(confirm: true);
        var error = Record.Exception(() => command.Execute(["--init", source, sorted]));
        if (error != null)
            Assert.IsType<IOException>(error);

        var indexPath = Path.Combine(sorted, "logs", "current.json");
        if (System.IO.File.Exists(indexPath))
        {
            var index = JsonSerializer.Deserialize<MediaIndex>(System.IO.File.ReadAllText(indexPath))!;
            foreach (var entry in index.Files.Where(f => f.Status == "active"))
            {
                var actualPath = Path.Combine(sorted, entry.SortedPath);
                Assert.True(System.IO.File.Exists(actualPath));
                Assert.Equal(entry.Hash, Sha256(actualPath));
            }
        }

    }

    [Fact]
    public void SyncDetectsOneMissingFileWhenTwoIndexedFilesShareAHash()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "duplicate");
        temp.File("source/b.jpg", "duplicate");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        System.IO.File.Delete(Path.Combine(sorted, "Photos", "jpg", "JPG1.jpg"));

        var (update, logger, _) = Create<SyncCommand>();
        update.Execute(["-n", sorted]);

        Assert.Contains(logger.Messages, m => m.Contains("Deleted: 1"));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("Everything is synchronized"));
    }

    [Fact]
    public void SyncDetectsNewFileWithAnAlreadyIndexedHash()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "duplicate");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        temp.File("sorted/Photos/jpg/JPG2.jpg", "duplicate");

        var (update, logger, _) = Create<SyncCommand>();
        update.Execute(["-n", sorted]);

        Assert.Contains(logger.Messages, m => m.Contains("New files: 1"));
    }

    [Fact]
    public void SyncMovesFileOutOfFolderThatOnlySharesTargetPrefix()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        var misplaced = temp.File("sorted/Photos/jpg_backup/new.jpg", "new image");

        var (update, _, _) = Create<SyncCommand>();
        update.Execute(["-y", sorted]);

        Assert.False(System.IO.File.Exists(misplaced));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(sorted, "Photos", "jpg")).Length);
    }

    [Fact]
    public void SyncRenumbersFilesWhenAnIndexedPathChanges()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "longer content");
        temp.File("source/b.jpg", "short");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        var jpgDir = Path.Combine(sorted, "Photos", "jpg");
        System.IO.File.Move(Path.Combine(jpgDir, "JPG1.jpg"), Path.Combine(jpgDir, "JPG3.jpg"));

        var (update, _, _) = Create<SyncCommand>();
        var exitCode = update.Execute(["-y", sorted]);

        Assert.Equal(0, exitCode);
        Assert.True(System.IO.File.Exists(Path.Combine(jpgDir, "JPG1.jpg")));
        Assert.True(System.IO.File.Exists(Path.Combine(jpgDir, "JPG2.jpg")));
        Assert.False(System.IO.File.Exists(Path.Combine(jpgDir, "JPG3.jpg")));
    }

    [Fact]
    public void SyncDoesNotRewriteIndexAfterUserDeclinesMisplacedFile()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        var indexPath = Path.Combine(sorted, "logs", "current.json");
        var originalIndex = System.IO.File.ReadAllText(indexPath);
        var misplaced = temp.File("sorted/loose.jpg", "new image");

        var (update, _, interaction) = Create<SyncCommand>();
        var exitCode = update.Execute([sorted]);

        Assert.Equal(1, exitCode);
        Assert.Equal(1, interaction.ConfirmationCount);
        Assert.True(System.IO.File.Exists(misplaced));
        Assert.Equal(originalIndex, System.IO.File.ReadAllText(indexPath));
    }

    [Fact]
    public void SyncRecordsDeletionWhenEntireExtensionFolderIsGone()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        Directory.Delete(Path.Combine(sorted, "Photos", "jpg"), recursive: true);

        var (update, _, _) = Create<SyncCommand>();
        var exitCode = update.Execute(["-y", sorted]);
        var indexPath = Path.Combine(sorted, "logs", "current.json");
        var index = JsonSerializer.Deserialize<MediaIndex>(System.IO.File.ReadAllText(indexPath))!;

        Assert.Equal(0, exitCode);
        Assert.Equal(0, index.Summary.TotalActiveFiles);
        Assert.Equal(1, index.Summary.TotalDeletedFiles);
    }

    [Fact]
    public void SyncDoesNotRenameUnrelatedFileInExtensionFolder()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var sorted = temp.Directory("sorted");
        temp.File("source/a.jpg", "original");

        var (init, _, _) = Create<SyncCommand>(confirm: true);
        init.Execute(["--init", source, sorted]);
        var notes = temp.File("sorted/Photos/jpg/notes.txt", "keep these notes");
        temp.File("sorted/Photos/jpg/new.jpg", "another image");

        var (update, _, _) = Create<SyncCommand>();
        update.Execute(["-y", sorted]);

        Assert.Equal("keep these notes", System.IO.File.ReadAllText(notes));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(sorted, "Photos", "jpg"), "*.jpg").Length);
    }

    [Fact]
    public void StrictFileScanFailsWhenDirectoryDisappears()
    {
        using var temp = new TemporaryDirectory();
        var files = new FileService(new TestLogger());

        Assert.Throws<DirectoryNotFoundException>(() =>
            files.GetFilteredFiles(temp.Path("missing"), strict: true).ToList()
        );
    }

    [Fact]
    public void DedupKeepsAllSourceCopiesIfKeeperCannotBeMoved()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var target = temp.Directory("target");
        var first = temp.File("source/a.jpg", "same contents");
        var second = temp.File("source/b.jpg", "same contents");
        var hashPrefix = Sha256(first)[..8];
        temp.File($"target/a.jpg_{hashPrefix}/a.jpg", "occupied");
        temp.File($"target/b.jpg_{hashPrefix}/b.jpg", "occupied");

        var (command, _, _) = Create<DedupCommand>(confirm: true);
        command.Execute([source, target]);

        Assert.True(System.IO.File.Exists(first));
        Assert.True(System.IO.File.Exists(second));
    }

    [Fact]
    public void DedupDryRunDoesNotCreateFilesOrDirectories()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        temp.File("source/a.jpg", "same");
        temp.File("source/b.jpg", "same");

        var (command, _, _) = Create<DedupCommand>();
        command.Execute(["-n", source]);

        Assert.False(Directory.Exists(Path.Combine(source, "logs")));
        Assert.False(Directory.Exists(Path.Combine(source, "duplicates")));
    }

    [Fact]
    public void DedupMovesKeeperAndThenDeletesDuplicates()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var target = temp.Directory("target");
        temp.File("source/a.jpg", "same contents");
        temp.File("source/b.jpg", "same contents");

        var (command, _, _) = Create<DedupCommand>(confirm: true);
        var exitCode = command.Execute([source, target]);

        Assert.Equal(0, exitCode);
        Assert.Empty(Directory.GetFiles(source, "*.jpg"));
        Assert.Single(Directory.GetFiles(target, "*.jpg", SearchOption.AllDirectories));
    }

    [Fact]
    public void CompareExtractHonorsDeclinedConfirmation()
    {
        using var temp = new TemporaryDirectory();
        var first = temp.Directory("first");
        var second = temp.Directory("second");
        var destination = temp.Path("extracted");
        var original = temp.File("first/unique.jpg", "unique");

        var (command, _, interaction) = Create<CompareCommand>();
        command.Execute(["--mode=hash", $"--extract={destination}", first, second]);

        Assert.True(interaction.ConfirmationCount > 0);
        Assert.True(System.IO.File.Exists(original));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void CompareByNameExtractHonorsDeclinedConfirmation()
    {
        using var temp = new TemporaryDirectory();
        temp.Directory("first/common");
        temp.Directory("second/common");
        var destination = temp.Path("extracted");
        var original = temp.File("first/common/unique.jpg", "unique");

        var (command, _, interaction) = Create<CompareCommand>();
        command.Execute([$"--extract={destination}", temp.Path("first"), temp.Path("second")]);

        Assert.Equal(1, interaction.ConfirmationCount);
        Assert.True(System.IO.File.Exists(original));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void CheckReportsInvalidJpegContent()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.Directory("source");
        var invalidJpeg = temp.Path("source/broken.jpg");
        System.IO.File.WriteAllBytes(invalidJpeg, Enumerable.Repeat((byte)0x41, 8192).ToArray());

        var (command, logger, _) = Create<CheckCommand>();
        var exitCode = command.Execute([source]);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(logger.Messages, m => m.Contains("broken.jpg") && m.Contains("CORRUPT"));
    }

    private static (T Command, TestLogger Logger, TestInteraction Interaction) Create<T>(
        bool confirm = false
    ) where T : BaseCommand
    {
        var config = new AppConfiguration();
        var logger = new TestLogger();
        var files = new FileService(logger);
        var hashes = new HashService(logger);
        var interaction = new TestInteraction(confirm);
        var command = (T)Activator.CreateInstance(typeof(T), logger, files, hashes, interaction, config)!;
        return (command, logger, interaction);
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"media-stow-tests-{Guid.NewGuid():N}"
        );

        public string Path(string relative) => System.IO.Path.Combine(_root, relative);

        public string Directory(string relative)
        {
            var path = Path(relative);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        public string File(string relative, string content)
        {
            var path = Path(relative);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(_root))
                System.IO.Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestInteraction(bool confirm) : IUserInteraction
    {
        public int ConfirmationCount { get; private set; }

        public bool Confirm(string message)
        {
            ConfirmationCount++;
            return confirm;
        }
    }

    private sealed class TestLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public void Info(string message) => _messages.Enqueue(message);
        public void Verbose(string message) => _messages.Enqueue(message);
        public void Warning(string message) => _messages.Enqueue(message);
        public void Error(string message) => _messages.Enqueue(message);
        public void Header(string title) => _messages.Enqueue(title);
        public void ShowProgress(int current, int total, string prefix = "Processing") { }
        public void ClearProgress() { }
    }
}
