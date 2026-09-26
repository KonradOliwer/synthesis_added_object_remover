using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace AddedObjectRemover;

internal enum MeshSource { NotFound, LooseFile, Archive }

/// <summary>
/// Reads mesh files the way the game resolves them: a loose file in the Data folder wins,
/// otherwise the highest-priority archive containing the path. Thread-safe: BSA file reads open
/// their own FileStream per read (BsaFileRecord.AsStream), so they need no lock.
/// </summary>
internal sealed class MeshFileSource
{
    private const string MeshesPrefix = "meshes\\";
    private const string DataPrefix = "data\\";

    private readonly string _dataPath;
    private readonly GameRelease _release;
    private readonly IReadOnlyList<ModKey> _loadOrder;
    private readonly MeshMessageLog _messages;
    private readonly Lazy<Dictionary<string, IArchiveFile>> _archiveIndex;
    private int _archivesIndexed;

    public MeshFileSource(string dataPath, GameRelease release, IReadOnlyList<ModKey> loadOrder, MeshMessageLog messages)
    {
        _dataPath = dataPath;
        _release = release;
        _loadOrder = loadOrder;
        _messages = messages;
        _archiveIndex = new Lazy<Dictionary<string, IArchiveFile>>(BuildArchiveIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int ArchivesIndexed => Volatile.Read(ref _archivesIndexed);

    /// <summary>Builds the archive index on the calling thread, so its warnings print in order and workers never wait on it.</summary>
    public void BuildArchiveIndexNow() => _ = _archiveIndex.Value;

    /// <summary>"meshes\..." form with backslashes; Model.File may or may not already contain "meshes\" (or even "data\meshes\").</summary>
    public static string NormalizeMeshPath(string givenPath)
    {
        var path = NormalizeArchivePath(givenPath);
        if (path.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[DataPrefix.Length..];
        }
        if (!path.StartsWith(MeshesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = MeshesPrefix + path;
        }
        return path;
    }

    private static string NormalizeArchivePath(string path) =>
        path.Trim().Replace('/', '\\').TrimStart('\\');

    public (byte[]? Bytes, MeshSource Source) Load(string meshPath)
    {
        if (TryReadLooseFile(meshPath) is { } looseBytes) return (looseBytes, MeshSource.LooseFile);
        if (TryExtractFromArchive(meshPath) is { } archiveBytes) return (archiveBytes, MeshSource.Archive);
        return (null, MeshSource.NotFound);
    }

    private byte[]? TryReadLooseFile(string meshPath)
    {
        var loosePath = Path.Combine(_dataPath, meshPath);
        try
        {
            return File.Exists(loosePath) ? File.ReadAllBytes(loosePath) : null;
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            _messages.Add(meshPath, $"  [mesh] could not read loose file {loosePath}: {ex.Message}");
            return null;
        }
    }

    private byte[]? TryExtractFromArchive(string meshPath)
    {
        if (!_archiveIndex.Value.TryGetValue(meshPath, out var archiveFile)) return null;
        try
        {
            return archiveFile.GetBytes();
        }
        catch (Exception ex) when (ExpectedFailures.IsCorruptArchive(ex))
        {
            _messages.Add(meshPath, $"  [mesh] could not extract {meshPath} from archive: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// One case-insensitive index of every .nif in the applicable archives. Archives are processed
    /// from lowest to highest priority, so later archives override earlier ones.
    /// </summary>
    private Dictionary<string, IArchiveFile> BuildArchiveIndex()
    {
        var index = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in GetArchivePathsInPriorityOrder())
        {
            try
            {
                AddArchiveMeshes(archivePath, index);
                Interlocked.Increment(ref _archivesIndexed);
            }
            catch (Exception ex) when (ExpectedFailures.IsCorruptArchive(ex))
            {
                Console.WriteLine($"  Warning: could not read archive {Path.GetFileName(archivePath)}: {ex.Message}");
            }
        }
        return index;
    }

    private void AddArchiveMeshes(string archivePath, Dictionary<string, IArchiveFile> index)
    {
        var reader = Archive.CreateReader(_release, new FilePath(archivePath));
        foreach (var file in reader.Files)
        {
            var path = NormalizeArchivePath(file.Path);
            if (path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            {
                index[path] = file;
            }
        }
    }

    /// <summary>
    /// Archives listed in the game INI (vanilla archives) first, then each plugin's archives in
    /// load order: "&lt;Plugin&gt;.bsa", then "&lt;Plugin&gt; - &lt;Suffix&gt;.bsa" by name.
    /// </summary>
    private List<string> GetArchivePathsInPriorityOrder()
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfExists(string fileName)
        {
            if (!seen.Add(fileName)) return;
            var fullPath = Path.Combine(_dataPath, fileName);
            if (File.Exists(fullPath)) ordered.Add(fullPath);
        }

        foreach (var fileName in GetIniArchiveNames()) AddIfExists(fileName);

        var extension = Archive.GetExtension(_release);
        foreach (var modKey in _loadOrder)
        {
            AddIfExists(modKey.Name + extension);
            foreach (var fileName in GetSuffixedArchiveNames(modKey.Name, extension)) AddIfExists(fileName);
        }

        return ordered;
    }

    private List<string> GetIniArchiveNames()
    {
        try
        {
            return Archive.GetIniListings(_release).Select(listing => listing.String).ToList();
        }
        catch (Exception ex) when (ExpectedFailures.IsUnreadableIni(ex))
        {
            Console.WriteLine($"  Warning: could not read archive list from game INI: {ex.Message}");
            return [];
        }
    }

    private List<string> GetSuffixedArchiveNames(string pluginStem, string extension)
    {
        try
        {
            return Directory.EnumerateFiles(_dataPath, $"{pluginStem} - *{extension}")
                .Select(fullPath => Path.GetFileName(fullPath))
                .OfType<string>()
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return []; // Unusual characters in a plugin name or an unreadable folder: suffixed archives are skipped.
        }
    }
}
