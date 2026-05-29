using MediaStow.Abstractions;
using MediaStow.Configuration;

namespace MediaStow.Commands;

public class HelpCommand : BaseCommand
{
    public HelpCommand(
        ILogger logger,
        IFileService fileService,
        IHashService hashService,
        IUserInteraction userInteraction,
        AppConfiguration config
    )
        : base(logger, fileService, hashService, userInteraction, config) { }

    public override string Name => "help";
    public override string Description => "Show help information";

    public override int Execute(string[] args)
    {
        Console.WriteLine(
            @"
MEDIA-STOW - Media file analysis and organization tool

Usage: media-stow <command> [options] <directories>

ANALYSIS:
  ext <dir>                 List file extensions
  empty <dir>               Find empty folders
  tree <dir>                Show folder structure
  check <dir>               Detect corrupt files

COMPARISON:
  compare <dir1> <dir2>               Compare folders by name
  compare --mode=hash <dir1> <dir2>   Compare by content hash
  extract-common <dir1> <dir2> <dest> Move common files from dir1, copy from dir2
  compare-logs <json> [json2]         Analyze/compare JSON indexes

VERIFICATION:
  verify <sorted>                     Quick structure check
  verify <sorted> <source>            Full verification with JSON report

ORGANIZATION:
  dedup [-n] <source> [target]        Find and remove duplicate files
  sync --init <source> [target]       Initial organization
  sync [-n] [-y] <media>              Synchronize and update index

OPTIONS:
  -n, --dry-run    Preview mode (no changes made)
  -v, --verbose    Show detailed output
  -q, --quiet      Show errors only
  -y, --yes        Auto-confirm all prompts
"
        );
        return 0;
    }
}
