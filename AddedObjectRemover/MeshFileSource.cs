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
    private const string ArchiveSuffixDelimiter = " - ";

    private readonly string _dataPath;
    private readonly GameRelease _release;
    private readonly IReadOnlyList<ModKey> _loadOrder;
    private readonly AssetProblemLog _problems;
    private readonly Lazy<Dictionary<string, IArchiveFile>> _archiveIndex;
    private int _archivesIndexed;

    public MeshFileSource(string dataPath, GameRelease release, IReadOnlyList<ModKey> loadOrder, AssetProblemLog problems)
    {
        _dataPath = dataPath;
        _release = release;
        _loadOrder = loadOrder;
        _problems = problems;
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
        if (ReadLooseFileOrNull(meshPath) is { } looseBytes) return (looseBytes, MeshSource.LooseFile);
        if (ExtractFromArchiveOrNull(meshPath) is { } archiveBytes) return (archiveBytes, MeshSource.Archive);
        return (null, MeshSource.NotFound);
    }

    private byte[]? ReadLooseFileOrNull(string meshPath)
    {
        var loosePath = Path.Combine(_dataPath, meshPath.Replace('\\', Path.DirectorySeparatorChar));
        try
        {
            return File.Exists(loosePath) ? File.ReadAllBytes(loosePath) : null;
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            _problems.Add(new AssetProblem(
                meshPath, AssetProblemKind.LooseFileUnreadable, $"  [mesh] could not read loose file {loosePath}: {ex.Message}"));
            return null;
        }
    }

    private byte[]? ExtractFromArchiveOrNull(string meshPath)
    {
        if (!_archiveIndex.Value.TryGetValue(meshPath, out var archiveFile)) return null;
        try
        {
            return archiveFile.GetBytes();
        }
        catch (Exception ex) when (ExpectedFailures.IsCorruptArchive(ex))
        {
            _problems.Add(new AssetProblem(
                meshPath, AssetProblemKind.ArchiveExtractFailed, $"  [mesh] could not extract {meshPath} from archive: {ex.GetType().Name}: {ex.Message}"));
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
                var archiveName = Path.GetFileName(archivePath);
                _problems.Add(new ArchiveProblem(
                    ArchiveProblemKind.ArchiveUnreadable, archiveName, $"  Warning: could not read archive {archiveName}: {ex.Message}"));
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
    /// load order, as matched by Mutagen's <see cref="Archive.IsApplicable"/> ("&lt;Plugin&gt;.bsa"
    /// first, then "&lt;Plugin&gt; - &lt;Suffix&gt;.bsa" by name). Only archives present in the Data
    /// folder are used.
    /// </summary>
    private List<string> GetArchivePathsInPriorityOrder()
    {
        var present = ListDataFolderArchives();
        var presentByStem = IndexByStem(present);
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfPresent(string fileName)
        {
            if (present.Contains(fileName) && seen.Add(fileName)) ordered.Add(Path.Combine(_dataPath, fileName));
        }

        foreach (var fileName in GetIniArchiveNames()) AddIfPresent(fileName);
        foreach (var modKey in _loadOrder)
        {
            foreach (var fileName in GetPluginArchiveNames(modKey, presentByStem)) AddIfPresent(fileName);
        }
        return ordered;
    }

    /// <summary>
    /// Archive file names keyed by every plugin name they could belong to: the name without
    /// extension, and the part before its last " - ". A superset of what
    /// <see cref="Archive.IsApplicable"/> accepts, so each plugin only checks its own few candidates.
    /// </summary>
    private static ILookup<string, string> IndexByStem(IEnumerable<string> archiveNames) =>
        archiveNames
            .SelectMany(fileName => GetStems(fileName).Select(stem => (Stem: stem, FileName: fileName)))
            .ToLookup(entry => entry.Stem, entry => entry.FileName, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> GetStems(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        yield return name;
        var delimiter = name.LastIndexOf(ArchiveSuffixDelimiter, StringComparison.Ordinal);
        if (delimiter >= 0) yield return name[..delimiter];
    }

    private IEnumerable<string> GetPluginArchiveNames(ModKey modKey, ILookup<string, string> archivesByStem)
    {
        var ownName = modKey.Name + Archive.GetExtension(_release);
        return archivesByStem[modKey.Name]
            .Where(fileName => Archive.IsApplicable(_release, modKey, new FileName(fileName)))
            .OrderBy(fileName => !fileName.Equals(ownName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(fileName => fileName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>File names of every archive in the Data folder; empty (with a warning) when the folder cannot be listed.</summary>
    private HashSet<string> ListDataFolderArchives()
    {
        try
        {
            return Directory.EnumerateFiles(_dataPath, "*" + Archive.GetExtension(_release))
                .Select(path => Path.GetFileName(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            _problems.Add(new ArchiveProblem(
                ArchiveProblemKind.DataFolderUnlistable, _dataPath, $"  Warning: could not list archives in {_dataPath}: {ex.Message}"));
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private List<string> GetIniArchiveNames()
    {
        try
        {
            return Archive.GetIniListings(_release).Select(listing => listing.String).ToList();
        }
        catch (Exception ex) when (ExpectedFailures.IsUnreadableIni(ex))
        {
            _problems.Add(new ArchiveProblem(
                ArchiveProblemKind.IniArchiveListUnreadable, "game INI", $"  Warning: could not read archive list from game INI: {ex.Message}"));
            return [];
        }
    }
}
