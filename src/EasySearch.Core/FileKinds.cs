namespace EasySearch.Core;

/// <summary>Maps the friendly "kind" filters (image, video, ...) to file extensions.</summary>
public static class FileKinds
{
    static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = Set("jpg jpeg png gif bmp webp svg ico tif tiff heic heif avif raw cr2 nef arw dng psd ai eps"),
        ["video"] = Set("mp4 mkv mov avi wmv webm m4v flv mpg mpeg 3gp ts mts"),
        ["audio"] = Set("mp3 wav flac aac ogg m4a wma opus aiff aif mid midi"),
        ["document"] = Set("pdf doc docx xls xlsx ppt pptx txt md rtf odt ods odp csv epub pages numbers key tex one"),
        ["code"] = Set("js ts jsx tsx mjs cjs py cs java c h cpp hpp cc go rs rb php html htm css scss less json xml yaml yml toml sql sh ps1 psm1 bat cmd swift kt lua r dart vue svelte ini cfg conf gradle sln slnx csproj props targets"),
        ["archive"] = Set("zip rar 7z tar gz bz2 xz tgz iso cab zst"),
        ["app"] = Set("exe msi lnk appx msix com scr appref-ms"),
    };

    public static IEnumerable<string> Names => Map.Keys;

    public static HashSet<string>? Extensions(string kind) => Map.GetValueOrDefault(Alias(kind));

    /// <summary>Extension without the dot, or empty when the name has none.</summary>
    public static ReadOnlySpan<char> ExtensionOf(ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? default : name[(dot + 1)..];
    }

    static string Alias(string kind) => kind.ToLowerInvariant() switch
    {
        "images" or "picture" or "pictures" or "pic" or "photo" or "photos" => "image",
        "videos" or "movie" or "movies" => "video",
        "music" or "sound" or "song" or "songs" => "audio",
        "documents" or "doc" or "docs" => "document",
        "source" or "dev" => "code",
        "archives" or "compressed" or "zip" => "archive",
        "apps" or "application" or "applications" or "exe" or "executable" => "app",
        var k => k,
    };

    static HashSet<string> Set(string extensions) =>
        new(extensions.Split(' '), StringComparer.OrdinalIgnoreCase);
}
