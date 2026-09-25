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
/// and falling back to the base record's Object Bounds (OBND), plus (for touch tests) the mesh's
/// root-space triangles. All results are cached per base FormKey and per normalized model path
/// (failures included).
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
    private readonly ConcurrentDictionary<string, Lazy<NifGeometry?>> _geometryByModel = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Bounds of a base plus the normalized model path its bounds came from (null for OBND/none).</summary>
    private sealed record BaseInfo(Box Box, string? ModelPath);

    /// <summary>Per model: bounds (null = unreadable) and, if requested at first read, the triangles.</summary>
    private sealed record ModelData(Box? Box, NifGeometry? Geometry);

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
        baseRef is { } reference ? GetBaseInfo(reference, keepGeometry: false).Box : Box.Zero;

    /// <summary>
    /// Resolves bounds for all given bases in parallel. With <paramref name="keepGeometry"/> the
    /// triangles of meshes read here are kept for <see cref="GetGeometry"/> (the same parse
    /// yields both bounds and triangles, so no mesh is read twice).
    /// </summary>
    public void WarmUp(IReadOnlyList<BaseRef> bases, bool keepGeometry, ParallelOptions options)
    {
        Parallel.ForEach(bases, options, reference => GetBaseInfo(reference, keepGeometry));
    }

    /// <summary>
    /// Root-space render triangles of the base's mesh, or null when its bounds did not come from a
    /// readable mesh (OBND fallback, NPCs, no model) or the mesh has no triangles.
    /// </summary>
    public NifGeometry? GetGeometry(BaseRef? baseRef)
    {
        if (baseRef is not { } reference) return null;
        var info = GetBaseInfo(reference, keepGeometry: true);
        if (info.ModelPath is not { } path) return null;
        var model = GetModelData(path, keepGeometry: true);
        if (model.Box == null) return null;
        var geometry = model.Geometry
            ?? _geometryByModel.GetOrAdd(
                path,
                p => new Lazy<NifGeometry?>(() => ReadGeometry(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return geometry is { TriangleCount: > 0 } ? geometry : null;
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

    private BaseInfo GetBaseInfo(BaseRef reference, bool keepGeometry) =>
        _byBase.GetOrAdd(
            reference.FormKey,
            _ => new Lazy<BaseInfo>(() => ComputeBaseInfo(reference, keepGeometry), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private BaseInfo ComputeBaseInfo(BaseRef baseRef, bool keepGeometry)
    {
        if (TryResolveBase(baseRef) is not { } record)
        {
            Interlocked.Increment(ref _basesUnresolved);
            return new BaseInfo(Box.Zero, null);
        }

        if (_useNif
            && record is IModeledGetter { Model: { } model }
            && !string.IsNullOrWhiteSpace(model.File.GivenPath))
        {
            var path = NormalizeMeshPath(model.File.GivenPath);
            if (GetModelData(path, keepGeometry).Box is { } nifBox)
            {
                Interlocked.Increment(ref _basesFromNif);
                return new BaseInfo(nifBox, path);
            }
            Interlocked.Increment(ref _basesNifFallbackToObnd);
        }

        if (record is IObjectBoundedOptionalGetter { ObjectBounds: { } bounds })
        {
            Interlocked.Increment(ref _basesFromObnd);
            return new BaseInfo(
                Box.FromCorners(
                    new Vector3(bounds.First.X, bounds.First.Y, bounds.First.Z),
                    new Vector3(bounds.Second.X, bounds.Second.Y, bounds.Second.Z)),
                null);
        }

        Interlocked.Increment(ref _basesWithoutBounds);
        return new BaseInfo(Box.Zero, null);
    }

    /// <summary>
    /// Cached bounds (and optionally triangles) of a mesh. Whether triangles are kept is decided by
    /// the first caller for a path; <see cref="GetGeometry"/> re-reads a mesh whose triangles were
    /// not kept (only happens for a mesh first seen through an other-mod object).
    /// </summary>
    private ModelData GetModelData(string path, bool keepGeometry) =>
        _byModel.GetOrAdd(
            path,
            p => new Lazy<ModelData>(() => ReadModel(p, keepGeometry), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private ModelData ReadModel(string path, bool keepGeometry)
    {
        Box? box = null;
        NifGeometry? geometry = null;
        var bytes = LoadMeshBytes(path, countSource: true);
        if (bytes == null)
        {
            Report(path, $"  [mesh] not found: {path}");
        }
        else if (NifBoundsReader.TryReadGeometry(bytes, keepGeometry, out var read, out var error)
                 && read != null
                 && Geometry.IsFinite(read.Min) && Geometry.IsFinite(read.Max))
        {
            box = Box.FromCorners(read.Min, read.Max);
            if (keepGeometry) geometry = read;
        }
        else
        {
            Report(path, $"  [mesh] unreadable: {path} ({error ?? "non-finite bounds"})");
        }

        if (box.HasValue) Interlocked.Increment(ref _modelsRead);
        else Interlocked.Increment(ref _modelsFailed);

        return new ModelData(box, geometry);
    }

    private NifGeometry? ReadGeometry(string path)
    {
        var bytes = LoadMeshBytes(path, countSource: false);
        return bytes != null && NifBoundsReader.TryReadGeometry(bytes, includeTriangles: true, out var geometry, out _)
            ? geometry
            : null;
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
