namespace MediaStow.Configuration;

public static class FilterConfiguration
{
    public static readonly HashSet<string> FilterExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ds_store",
        ".aae",
        ".json",
        ".ini",
        ".db",
        ".thm",
    };

    public static readonly HashSet<string> FilterFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".spotlight-v100",
        ".trashes",
        ".fseventsd",
        ".temporaryitems",
        "$recycle.bin",
        "system volume information",
        "@eadir",
    };

    public static readonly string[] FilterPrefixes = { "._", ".sync" };
}
