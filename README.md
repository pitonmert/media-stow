# MediaStow

MediaStow is a high-performance command-line interface (CLI) utility built with .NET, designed to seamlessly sync, verify, deduplicate, and organize your media files (photos, videos, and audio). It uses content-based hashing (SHA-256) to guarantee data integrity, preventing duplicate files and ensuring that your media library is strictly organized without data loss.

## Features

- **Media Synchronization (`sync`)**: Sync media files from a source directory to a target library, maintaining a structured category and extension-based organization. 
- **Deduplication (`dedup`)**: Identify and safely eliminate duplicate media files across your directories using robust SHA-256 hash comparisons.
- **Verification (`verify`)**: Verify the structural integrity of your sorted media directory to ensure that no files are missing, misplaced, or incorrectly ranked.
- **Common Extraction (`extract-common`)**: Compare two directories and safely extract/move files that exist in both locations to target directories.
- **Log Comparison (`compare-logs`)**: Analyze and compare large JSON log outputs to track differences in sync states.
- **Health Check (`check`)**: Read media files and screen common formats for invalid signatures. This is a basic check and does not fully decode media content.

## Installation

Ensure you have the [.NET SDK](https://dotnet.microsoft.com/download) installed.

Clone the repository and build the project:

```bash
git clone https://github.com/pitonmert/media-stow.git
cd media-stow
dotnet build
```

You can run the utility directly via the CLI:

```bash
dotnet run --project MediaStow.csproj -- --help
```

Publish a framework-dependent executable (the .NET 10 runtime must be installed):

```bash
dotnet publish MediaStow.csproj -c Release -o dist/media-stow
./dist/media-stow/media-stow help
```

From another directory, invoke the executable by its absolute path.

## Usage

MediaStow uses a verb-based command interface. You can pass `-v` for verbose output or `-q` for quiet mode.

**Sync Media:**
```bash
media-stow sync --init <sourceDir> [targetDir]   # initial setup
media-stow sync [-n] [-y] <mediaDir>             # update/sync
```

**Deduplicate Files:**
```bash
media-stow dedup [-n] <sourceDir> [targetDir]
```

Use `dedup -n -v <sourceDir>` to preview the SHA-256 groups and individual paths without changing the source directory. A real `dedup` run moves one file from each group into `<sourceDir>/duplicates` (or the specified target) and deletes the remaining copies after confirmation. Matching names or sizes alone do not establish that files are duplicates.

Recursive scans skip symbolic links to files and directories. Links are not treated as independent content copies by `dedup`.

**Verify Structure:**
```bash
media-stow verify <sortedDir> [sourceDir]
```

**Extract Common Files:**
```bash
media-stow extract-common <icloudDir> <mediaDir> <targetBase>
```

`compare --extract=<directory>` asks for confirmation before moving differing files.
`check` and `verify` return a nonzero exit code when they find problems.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
