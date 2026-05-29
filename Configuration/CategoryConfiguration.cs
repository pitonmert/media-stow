namespace MediaStow.Configuration;

public static class CategoryConfiguration
{
    public static readonly Dictionary<string, string> ExtToCategory = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        // Photos
        ["jpg"] = "photos",
        ["jpeg"] = "photos",
        ["png"] = "photos",
        ["heic"] = "photos",
        ["cr2"] = "photos",
        ["webp"] = "photos",
        ["gif"] = "photos",
        ["bmp"] = "photos",
        ["tiff"] = "photos",
        ["tif"] = "photos",
        ["raw"] = "photos",
        ["nef"] = "photos",
        ["arw"] = "photos",
        ["dng"] = "photos",
        ["orf"] = "photos",

        // Videos
        ["mov"] = "videos",
        ["mp4"] = "videos",
        ["3gp"] = "videos",
        ["mkv"] = "videos",
        ["avi"] = "videos",
        ["wmv"] = "videos",
        ["flv"] = "videos",
        ["webm"] = "videos",
        ["m4v"] = "videos",
        ["mts"] = "videos",
        ["m2ts"] = "videos",
        ["ts"] = "videos",

        // Audio
        ["m4a"] = "audio",
        ["mp3"] = "audio",
        ["wav"] = "audio",
        ["flac"] = "audio",
        ["aac"] = "audio",
        ["wma"] = "audio",
        ["ogg"] = "audio",
        ["aiff"] = "audio",
    };

    public static readonly Dictionary<string, string> CategoryDisplay = new()
    {
        ["photos"] = "Photos",
        ["videos"] = "Videos",
        ["audio"] = "Audio",
    };

    public static string? GetCategory(string extension)
    {
        var ext = extension.TrimStart('.').ToLower();
        return ExtToCategory.TryGetValue(ext, out var cat) ? cat : null;
    }

    public static string GetCategoryDisplay(string category)
    {
        return CategoryDisplay.TryGetValue(category, out var display) ? display : category;
    }

    public static string[] GetExtensionsForCategory(string category)
    {
        return ExtToCategory.Where(kv => kv.Value == category).Select(kv => kv.Key).ToArray();
    }
}
