using System.Collections.Concurrent;
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
/// and falling back to the base record's Object Bounds (OBND), whether a base is invisible, and
/// (for touch tests, read on demand and not cached) the mesh's root-space triangles. Bounds and
/// visibility are cached per base FormKey and per normalized model path (failures included).
///
/// Thread-safe: every cache entry is a <see cref="Lazy{T}"/> in a ConcurrentDictionary, so each
/// base is resolved and each mesh is read and parsed exactly once even when many threads ask for
/// it at the same time. Mutagen's load-order link cache is documented as multithread safe, and
/// BSA file reads open their own FileStream per read (BsaFileRecord.AsStream), so neither needs a
/// lock. Verbose mesh messages are queued and printed in path order by <see cref="PrintMessages"/>.
/// </summary>
internal sealed class ObjectBoundsProvider
{
    private const string MeshesPrefix = "meshes\\";

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly bool _useNif;
    private readonly bool _verbose;
    private readonly string _dataPath;
    private readonly ConcurrentDictionary<FormKey, Lazy<BaseInfo>> _byBase = new();
    private readonly ConcurrentDictionary<string, Lazy<ModelData>> _byModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Dictionary<string, IArchiveFile>> _archiveIndex;
    private readonly ConcurrentQueue<(string Path, string Message)> _messages = new();

    private int _basesFromNif;
    private int _basesNifFallbackToObnd;
    private int _basesFromObnd;
    private int _basesWithoutBounds;
    private int _basesUnresolved;
    private int _modelsRead;
    private int _modelsFailed;
    private int _modelsFromLooseFiles;
    private int _modelsFromArchives;
    private int _archivesIndexed;

    public ObjectBoundsProvider(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, bool useNif, bool verbose)
    {
        _state = state;
        _useNif = useNif;
        _verbose = verbose;
        _dataPath = state.DataFolderPath.Path;
        _archiveIndex = new Lazy<Dictionary<string, IArchiveFile>>(BuildArchiveIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Bounds of a base, the normalized model path its bounds came from (null for OBND/none), and
    /// why the base is never visible in game (null when it may be visible).
    /// </summary>
    private sealed record BaseInfo(Box Box, string? ModelPath, string? InvisibleReason);

    /// <summary>Per model: bounds (null = unreadable or no render geometry) and the read outcome.</summary>
    private sealed record ModelData(Box? Box, NifReadStatus Status);

    // Per unique base object
    public int BasesFromNif => Volatile.Read(ref _basesFromNif);
    public int BasesNifFallbackToObnd => Volatile.Read(ref _basesNifFallbackToObnd);
    public int BasesFromObnd => Volatile.Read(ref _basesFromObnd);
    public int BasesWithoutBounds => Volatile.Read(ref _basesWithoutBounds);
    public int BasesUnresolved => Volatile.Read(ref _basesUnresolved);

    // Per unique model path
    public int ModelsRead => Volatile.Read(ref _modelsRead);
    public int ModelsFailed => Volatile.Read(ref _modelsFailed);
    public int ModelsFromLooseFiles => Volatile.Read(ref _modelsFromLooseFiles);
    public int ModelsFromArchives => Volatile.Read(ref _modelsFromArchives);

    public int ArchivesIndexed => Volatile.Read(ref _archivesIndexed);

    /// <summary>
    /// Builds the archive index now (on the calling thread), so its warnings are printed in order
    /// and worker threads never wait on it. Does nothing when meshes are not used.
    /// </summary>
    public void PrepareArchives()
    {
        if (_useNif) _ = _archiveIndex.Value;
    }

    /// <summary>
    /// Local-space bounds of the given base object. Unknown/missing bases yield a zero-size box at the origin.
    /// </summary>
    public Box GetLocalBox(BaseRef? baseRef) =>
        baseRef is { } reference ? GetBaseInfo(reference).Box : Box.Zero;

    /// <summary>Resolves bounds for all given bases in parallel.</summary>
    public void WarmUp(IReadOnlyList<BaseRef> bases, ParallelOptions options)
    {
        Parallel.ForEach(bases, options, reference => GetBaseInfo(reference));
    }

    /// <summary>
    /// Normalized path of the mesh the base's bounds came from, or null when they did not come
    /// from a readable mesh (OBND fallback, NPCs, no model).
    /// </summary>
    public string? GetMeshPath(BaseRef? baseRef) =>
        baseRef is { } reference ? GetBaseInfo(reference).ModelPath : null;

    /// <summary>
    /// Why an other-mod object with this base can never be seen or collided with (a light or sound
    /// marker, a decal, a mesh with only marker geometry, ...), or null when it may be visible.
    /// <paramref name="isPrimitive"/>: the placed reference is a primitive box (trigger/activator
    /// volume), which only counts when its base has a visible mesh.
    /// </summary>
    public string? GetInvisibleReason(BaseRef? baseRef, bool isPrimitive)
    {
        if (baseRef is not { } reference) return null;
        var info = GetBaseInfo(reference);
        if (info.InvisibleReason != null) return info.InvisibleReason;
        return isPrimitive && info.ModelPath == null ? "trigger/activator box without visible mesh" : null;
    }

    /// <summary>
    /// Reads the root-space render triangles of a mesh (not cached; the caller keeps what it
    /// needs). Null when the mesh cannot be read or has no triangles.
    /// </summary>
    public NifGeometry? ReadGeometry(string meshPath)
    {
        var bytes = LoadMeshBytes(meshPath, countSource: false);
        return bytes != null
               && NifBoundsReader.ReadGeometry(bytes, includeTriangles: true, out var geometry, out _) == NifReadStatus.Success
               && geometry is { TriangleCount: > 0 }
            ? geometry
            : null;
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

    /// <summary>Prints queued verbose mesh messages sorted by mesh path, then clears the queue.</summary>
    public void PrintMessages()
    {
        var messages = new List<(string Path, string Message)>();
        while (_messages.TryDequeue(out var message)) messages.Add(message);
        messages.Sort((a, b) =>
        {
            var byPath = StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
            return byPath != 0 ? byPath : StringComparer.Ordinal.Compare(a.Message, b.Message);
        });
        foreach (var (_, message) in messages) Console.WriteLine(message);
    }

    private BaseInfo GetBaseInfo(BaseRef reference) =>
        _byBase.GetOrAdd(
            reference.FormKey,
            _ => new Lazy<BaseInfo>(() => ComputeBaseInfo(reference), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private BaseInfo ComputeBaseInfo(BaseRef baseRef)
    {
        if (TryResolveBase(baseRef) is not { } record)
        {
            Interlocked.Increment(ref _basesUnresolved);
            return new BaseInfo(Box.Zero, null, null);
        }

        var modelPath = record is IModeledGetter { Model: { } model } && !string.IsNullOrWhiteSpace(model.File.GivenPath)
            ? model.File.GivenPath
            : null;
        var meshWithoutGeometry = false;
        if (_useNif && modelPath != null)
        {
            var path = NormalizeMeshPath(modelPath);
            var data = GetModelData(path);
            if (data.Box is { } nifBox)
            {
                Interlocked.Increment(ref _basesFromNif);
                return new BaseInfo(nifBox, path, GetInvisibleReason(record, hasModel: true, meshWithoutGeometry: false, nifBox));
            }
            meshWithoutGeometry = data.Status == NifReadStatus.NoRenderGeometry;
            Interlocked.Increment(ref _basesNifFallbackToObnd);
        }

        if (record is IObjectBoundedOptionalGetter { ObjectBounds: { } bounds })
        {
            Interlocked.Increment(ref _basesFromObnd);
            var box = Box.FromCorners(
                new Vector3(bounds.First.X, bounds.First.Y, bounds.First.Z),
                new Vector3(bounds.Second.X, bounds.Second.Y, bounds.Second.Z));
            return new BaseInfo(box, null, GetInvisibleReason(record, modelPath != null, meshWithoutGeometry, box));
        }

        Interlocked.Increment(ref _basesWithoutBounds);
        return new BaseInfo(Box.Zero, null, GetInvisibleReason(record, modelPath != null, meshWithoutGeometry, Box.Zero));
    }

    /// <summary>
    /// Structural invisibility of a base: record types that never render, a mesh that parsed but
    /// has no visible render geometry (marker meshes), or no mesh at all and zero-size bounds.
    /// NPCs always count as visible.
    /// </summary>
    private static string? GetInvisibleReason(IMajorRecordGetter record, bool hasModel, bool meshWithoutGeometry, Box bounds)
    {
        switch (record)
        {
            case INpcGetter: return null;
            case ILightGetter when !hasModel: return "light without mesh";
            case ISoundMarkerGetter: return "sound marker";
            case IAcousticSpaceGetter: return "acoustic space";
            case ITextureSetGetter: return "texture set (decal)";
            case IIdleMarkerGetter: return "idle marker";
        }
        if (meshWithoutGeometry) return "mesh without visible geometry (marker)";
        if (!hasModel && bounds.Size == Vector3.Zero) return "no mesh and zero bounds";
        return null;
    }

    /// <summary>Cached bounds of a mesh.</summary>
    private ModelData GetModelData(string path) =>
        _byModel.GetOrAdd(
            path,
            p => new Lazy<ModelData>(() => ReadModel(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private ModelData ReadModel(string path)
    {
        Box? box = null;
        var status = NifReadStatus.Failed;
        var bytes = LoadMeshBytes(path, countSource: true);
        if (bytes == null)
        {
            Report(path, $"  [mesh] not found: {path}");
        }
        else
        {
            status = NifBoundsReader.ReadGeometry(bytes, includeTriangles: false, out var read, out var error);
            if (status == NifReadStatus.Success
                && read != null
                && Geometry.IsWithinLimits(read.Min) && Geometry.IsWithinLimits(read.Max))
            {
                box = Box.FromCorners(read.Min, read.Max);
            }
            else
            {
                if (status == NifReadStatus.Success) status = NifReadStatus.Failed;
                Report(path, $"  [mesh] unreadable: {path} ({error ?? "bounds out of range"})");
            }
        }

        if (box.HasValue) Interlocked.Increment(ref _modelsRead);
        else Interlocked.Increment(ref _modelsFailed);

        return new ModelData(box, status);
    }

    private void Report(string path, string message)
    {
        if (_verbose) _messages.Enqueue((path, message));
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

    private byte[]? LoadMeshBytes(string meshPath, bool countSource)
    {
        // Loose files win over archives, as in the game.
        var loosePath = Path.Combine(_dataPath, meshPath);
        try
        {
            if (File.Exists(loosePath))
            {
                var bytes = File.ReadAllBytes(loosePath);
                if (countSource) Interlocked.Increment(ref _modelsFromLooseFiles);
                return bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(meshPath, $"  [mesh] could not read loose file {loosePath}: {ex.Message}");
        }

        if (_archiveIndex.Value.TryGetValue(meshPath, out var archiveFile))
        {
            try
            {
                // Each BSA file read opens its own stream, so concurrent reads are safe.
                var bytes = archiveFile.GetBytes();
                if (countSource) Interlocked.Increment(ref _modelsFromArchives);
                return bytes;
            }
            catch (Exception ex)
            {
                Report(meshPath, $"  [mesh] could not extract {meshPath} from archive: {ex.Message}");
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
                Interlocked.Increment(ref _archivesIndexed);
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
