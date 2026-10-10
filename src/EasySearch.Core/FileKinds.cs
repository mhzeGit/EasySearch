namespace EasySearch.Core;

/// <summary>A friendly name for a group of file suffixes: a kind such as "image" or a tool such as "blender".</summary>
/// <param name="Name">What the user types after <c>type:</c>, lower case without spaces.</param>
/// <param name="Label">How it is shown in the interface.</param>
/// <param name="Extensions">The suffixes it stands for, without dots.</param>
/// <param name="IsKind">True for broad kinds (image, video, 3d ...), false for tools and formats.</param>
public sealed record NamedType(string Name, string Label, string[] Extensions, bool IsKind);

/// <summary>
/// Maps the friendly names accepted after <c>type:</c> (image, video, 3d, blender, photoshop ...) to
/// file extensions. Anything not listed here is treated as a plain suffix by the query parser, so
/// <c>type:fbx</c> and <c>type:.fbx</c> both work.
/// </summary>
public static class FileKinds
{
    // Name, label, extensions, other names it answers to (separated by |). Spaces, dashes and
    // underscores are ignored when matching, so "3D models" and "3d-models" are the same word.
    static readonly (string Name, string Label, string Extensions, string Aliases, bool Kind)[] Entries =
    [
        // Broad kinds -- these also drive the type drop-down.
        ("image", "Images", "jpg jpeg png gif bmp webp svg ico tif tiff heic heif avif raw cr2 nef arw dng psd ai eps", "images|picture|pictures|pic|pics|photo|photos", true),
        ("video", "Video", "mp4 mkv mov avi wmv webm m4v flv mpg mpeg 3gp ts mts", "videos|movie|movies|film|films", true),
        ("audio", "Audio", "mp3 wav flac aac ogg m4a wma opus aiff aif mid midi", "music|sound|sounds|song|songs", true),
        ("document", "Documents", "pdf doc docx xls xlsx ppt pptx txt md rtf odt ods odp csv epub pages numbers key tex one", "documents|doc|docs", true),
        ("code", "Code", "js ts jsx tsx mjs cjs py cs java c h cpp hpp cc go rs rb php html htm css scss less json xml yaml yml toml sql sh ps1 psm1 bat cmd swift kt lua r dart vue svelte ini cfg conf gradle sln slnx csproj props targets", "source|dev|script|scripts", true),
        ("archive", "Archives", "zip rar 7z tar gz bz2 xz tgz iso cab zst", "archives|compressed|zip", true),
        ("app", "Apps", "exe msi lnk appx msix com scr appref-ms", "apps|application|applications|exe|executable|executables|program|programs", true),
        ("3d", "3D models", "fbx obj blend blend1 gltf glb stl 3ds dae ply usd usda usdc usdz abc c4d max ma mb x3d 3mf wrl zpr ztl skp", "3dmodel|3dmodels|model|models|mesh|meshes|3dfile|3dfiles", true),
        ("cad", "CAD drawings", "dwg dxf step stp iges igs sldprt sldasm ipt iam f3d", "drawing|drawings|cadfile|cadfiles", true),
        ("spreadsheet", "Spreadsheets", "xls xlsx xlsm xlsb ods csv tsv", "spreadsheets|sheet|sheets", true),
        ("presentation", "Presentations", "ppt pptx pptm pps ppsx odp key", "presentations|slides|slide|slideshow", true),
        ("font", "Fonts", "ttf otf woff woff2 fon", "fonts|typeface|typefaces", true),
        ("ebook", "E-books", "epub mobi azw azw3", "ebooks|book|books", true),
        ("database", "Databases", "db sqlite sqlite3 mdb accdb", "databases|sqlite", true),
        ("text", "Text files", "txt log md markdown rtf", "texts|plaintext", true),
        ("raw", "Camera RAW", "raw cr2 cr3 nef arw dng raf orf rw2", "cameraraw|rawphoto|rawphotos", true),

        // Tools and formats, by the name people know them by.
        ("blender", "Blender", "blend blend1 blend2", "blend", false),
        ("photoshop", "Photoshop", "psd psb", "ps|psd", false),
        ("illustrator", "Illustrator", "ai", "", false),
        ("indesign", "InDesign", "indd idml", "", false),
        ("figma", "Figma", "fig", "", false),
        ("sketchup", "SketchUp", "skp", "", false),
        ("maya", "Maya", "ma mb", "", false),
        ("3dsmax", "3ds Max", "max", "autodesk3dsmax", false),
        ("cinema4d", "Cinema 4D", "c4d", "c4d", false),
        ("zbrush", "ZBrush", "zpr ztl", "", false),
        ("unity", "Unity", "unity unitypackage prefab", "", false),
        ("unreal", "Unreal Engine", "uasset umap uproject", "ue|ue4|ue5", false),
        ("godot", "Godot", "gd tscn tres godot", "", false),
        ("premiere", "Premiere Pro", "prproj", "", false),
        ("aftereffects", "After Effects", "aep", "", false),
        ("davinci", "DaVinci Resolve", "drp", "resolve", false),
        ("ableton", "Ableton Live", "als", "", false),
        ("flstudio", "FL Studio", "flp", "", false),
        ("word", "Word", "doc docx docm dot dotx", "msword", false),
        ("excel", "Excel", "xls xlsx xlsm xlsb xlt xltx", "msexcel", false),
        ("powerpoint", "PowerPoint", "ppt pptx pptm pps ppsx potx", "msppt|ppt", false),
        ("pdf", "PDF", "pdf", "acrobat", false),
        ("python", "Python", "py pyw pyi ipynb", "py", false),
        ("javascript", "JavaScript", "js mjs cjs jsx", "js|node", false),
        ("typescript", "TypeScript", "ts tsx", "tsx", false),
        ("csharp", "C#", "cs csx csproj", "c#|dotnet", false),
        ("cpp", "C / C++", "c cc cpp cxx h hpp hxx", "c++|c", false),
        ("java", "Java", "java jar class", "", false),
        ("web", "Web pages", "html htm css js mjs", "website", false),
    ];

    static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> NameOf = new(StringComparer.OrdinalIgnoreCase);

    static FileKinds()
    {
        foreach (var (name, _, extensions, _, _) in Entries)
        {
            Map[name] = new HashSet<string>(extensions.Split(' '), StringComparer.OrdinalIgnoreCase);
            NameOf[Normalize(name)] = name;
        }
        // Aliases never override a real name ("c" is an alias of cpp, but "cs" stays a suffix).
        foreach (var (name, _, _, aliases, _) in Entries)
            foreach (var alias in aliases.Split('|', StringSplitOptions.RemoveEmptyEntries))
                NameOf.TryAdd(Normalize(alias), name);
    }

    /// <summary>Everything the type box can suggest, kinds first.</summary>
    public static IReadOnlyList<NamedType> Catalog { get; } =
        [.. Entries.Select(e => new NamedType(e.Name, e.Label, e.Extensions.Split(' '), e.Kind))];

    /// <summary>The broad kinds (image, video, 3d ...).</summary>
    public static IEnumerable<string> Names => Entries.Where(e => e.Kind).Select(e => e.Name);

    /// <summary>The suffixes behind a kind or tool name, or null when the word is not a known name.</summary>
    public static HashSet<string>? Extensions(string kind) =>
        NameOf.TryGetValue(Normalize(kind), out var name) ? Map[name] : null;

    /// <summary>Extension without the dot, or empty when the name has none.</summary>
    public static ReadOnlySpan<char> ExtensionOf(ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? default : name[(dot + 1)..];
    }

    static string Normalize(string word) =>
        string.Concat(word.Where(c => c is not (' ' or '-' or '_'))).ToLowerInvariant();
}
