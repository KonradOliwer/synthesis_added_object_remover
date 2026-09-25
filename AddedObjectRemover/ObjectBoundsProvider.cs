using System.Numerics;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// A placed record's base object: its FormKey plus the record type of the placed record's base
/// link (e.g. IPlaceableObjectGetter for REFR, INpcGetter for ACHR), used for narrowly typed
/// link-cache lookups.
/// </summary>
internal readonly record struct BaseRef(FormKey FormKey, Type LinkType);

/// <summary>
/// Provides the local-space bounding box of base objects, measured from the NIF mesh when possible
/// and falling back to the base record's Object Bounds (OBND). All results are cached per base
/// FormKey and per normalized model path (failures included).
/// </summary>
internal sealed class ObjectBoundsProvider
{
    private const string MeshesPrefix = "meshes\\";

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly bool _useNif;
    private readonly bool _verbose;
    private readonly string _dataPath;
    private readonly Dictionary<FormKey, Box> _byBase = new();
    private readonly Dictionary<string, Box?> _byModel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, IArchiveFile>? _archiveIndex;

    public ObjectBoundsProvider(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, bool useNif, bool verbose)
    {
        _state = state;
        _useNif = useNif;
        _verbose = verbose;
        _dataPath = state.DataFolderPath.Path;
    }

    // Per unique base object
    public int BasesFromNif { get; private set; }
    public int BasesNifFallbackToObnd { get; private set; }
    public int BasesFromObnd { get; private set; }
    public int BasesWithoutBounds { get; private set; }
    public int BasesUnresolved { get; private set; }

    // Per unique model path
    public int ModelsRead { get; private set; }
    public int ModelsFailed { get; private set; }
    public int ModelsFromLooseFiles { get; private set; }
    public int ModelsFromArchives { get; private set; }

    public int ArchivesIndexed { get; private set; }

    /// <summary>
    /// Local-space bounds of the given base object. Unknown/missing bases yield a zero-size box at the origin.
    /// </summary>
    public Box GetLocalBox(BaseRef? baseRef)
    {
        if (baseRef is not { } reference) return Box.Zero;
        if (_byBase.TryGetValue(reference.FormKey, out var cached)) return cached;
        var box = ComputeBaseBox(reference);
        _byBase[reference.FormKey] = box;
        return box;
    }

    /// <summary>
    /// Resolves a base record through the placed record's own base-link type. Mutagen's
    /// ImmutableLoadOrderLinkCacheCategory keeps one lazily filled cache per queried type: for a
    /// link interface such as IPlaceableObjectGetter it enumerates only the mapped registrations'
    /// top-level groups (via IMetaInterfaceMapGetter), and for a concrete getter such as
    /// INpcGetter only that group. IMajorRecordGetter would instead enumerate every record of every
    /// mod, including all cell children, so it is never used here.
    /// </summary>
    public IMajorRecordGetter? TryResolveBase(BaseRef baseRef) =>
        _state.LinkCache.TryResolve(baseRef.FormKey, baseRef.LinkType, out var record) ? record : null;

    private Box ComputeBaseBox(BaseRef baseRef)
    {
        if (TryResolveBase(baseRef) is not { } record)
        {
            BasesUnresolved++;
            return Box.Zero;
        }

        if (_useNif
            && record is IModeledGetter { Model: { } model }
            && !string.IsNullOrWhiteSpace(model.File.GivenPath))
        {
            if (GetModelBox(model.File.GivenPath) is { } nifBox)
            {
                BasesFromNif++;
                return nifBox;
            }
            BasesNifFallbackToObnd++;
        }

        if (record is IObjectBoundedOptionalGetter { ObjectBounds: { } bounds })
        {
            BasesFromObnd++;
            return Box.FromCorners(
                new Vector3(bounds.First.X, bounds.First.Y, bounds.First.Z),
                new Vector3(bounds.Second.X, bounds.Second.Y, bounds.Second.Z));
        }

        BasesWithoutBounds++;
        return Box.Zero;
    }

    private Box? GetModelBox(string givenPath)
    {
        var path = NormalizeMeshPath(givenPath);
        if (_byModel.TryGetValue(path, out var cached)) return cached;

        Box? result = null;
        var bytes = LoadMeshBytes(path);
        if (bytes == null)
        {
            if (_verbose) Console.WriteLine($"  [mesh] not found: {path}");
        }
        else if (NifBoundsReader.TryReadBounds(bytes, out var min, out var max, out var error)
                 && Geometry.IsFinite(min) && Geometry.IsFinite(max))
        {
            result = Box.FromCorners(min, max);
        }
        else if (_verbose)
        {
            Console.WriteLine($"  [mesh] unreadable: {path} ({error ?? "non-finite bounds"})");
        }

        if (result.HasValue) ModelsRead++;
        else ModelsFailed++;

        _byModel[path] = result;
        return result;
    }

    /// <summary>
    /// Normalizes a model path to "meshes\..." form with backslashes. Model.File may or may not
    /// already contain the "meshes\" (or even "data\meshes\") prefix.
    /// </summary>
    private static string NormalizeMeshPath(string givenPath)
    {
        var path = NormalizeArchivePath(givenPath);
        if (path.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
        {
            path = path["data\\".Length..];
        }
        if (!path.StartsWith(MeshesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = MeshesPrefix + path;
        }
        return path;
    }

    private static string NormalizeArchivePath(string path) =>
        path.Trim().Replace('/', '\\').TrimStart('\\');

    private byte[]? LoadMeshBytes(string meshPath)
    {
        // Loose files win over archives, as in the game.
        var loosePath = Path.Combine(_dataPath, meshPath);
        try
        {
            if (File.Exists(loosePath))
            {
                var bytes = File.ReadAllBytes(loosePath);
                ModelsFromLooseFiles++;
                return bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_verbose) Console.WriteLine($"  [mesh] could not read loose file {loosePath}: {ex.Message}");
        }

        _archiveIndex ??= BuildArchiveIndex();
        if (_archiveIndex.TryGetValue(meshPath, out var archiveFile))
        {
            try
            {
                var bytes = archiveFile.GetBytes();
                ModelsFromArchives++;
                return bytes;
            }
            catch (Exception ex)
            {
                if (_verbose) Console.WriteLine($"  [mesh] could not extract {meshPath} from archive: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Builds one case-insensitive index of every .nif in the applicable archives. Archives are
    /// processed from lowest to highest priority (INI-listed archives first, then plugin archives in
    /// load order), so later archives override earlier ones.
    /// </summary>
    private Dictionary<string, IArchiveFile> BuildArchiveIndex()
    {
        var index = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in GetArchivePathsInPriorityOrder())
        {
            try
            {
                var reader = Archive.CreateReader(_state.GameRelease, new FilePath(archivePath));
                foreach (var file in reader.Files)
                {
                    var path = NormalizeArchivePath(file.Path);
                    if (path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                    {
                        index[path] = file;
                    }
                }
                ArchivesIndexed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: could not read archive {Path.GetFileName(archivePath)}: {ex.Message}");
            }
        }
        return index;
    }

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

        // Archives listed in the game INI (vanilla archives) load first.
        try
        {
            foreach (var iniListing in Archive.GetIniListings(_state.GameRelease))
            {
                AddIfExists(iniListing.String);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: could not read archive list from game INI: {ex.Message}");
        }

        // Then each plugin's archives in load order: "<Plugin>.bsa" and "<Plugin> - <Suffix>.bsa".
        var extension = Archive.GetExtension(_state.GameRelease);
        foreach (var listing in _state.LoadOrder.ListedOrder)
        {
            var stem = listing.ModKey.Name;
            AddIfExists(stem + extension);
            try
            {
                foreach (var file in Directory.EnumerateFiles(_dataPath, $"{stem} - *{extension}")
                             .Select(fullPath => Path.GetFileName(fullPath))
                             .OfType<string>()
                             .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    AddIfExists(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unusual characters in a plugin name or an unreadable folder: just skip suffixed archives.
            }
        }

        return ordered;
    }
}
