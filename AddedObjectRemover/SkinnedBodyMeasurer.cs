namespace AddedObjectRemover;

/// <param name="Meshes">The measured meshes, joined in path order.</param>
internal sealed record SkinnedBodyMeasurement(string Meshes, SkinnedBodySize Size);

/// <summary>
/// Thread-safe cache of <see cref="WaistBandSizing"/> per set of body meshes (the addons one body
/// armour gives one race and sex). Each set is read and measured once; failures are cached too.
/// </summary>
internal sealed class SkinnedBodyMeasurer(Func<string, NifGeometry?> readGeometry)
{
    private const string MeshSeparator = " + ";

    private readonly LazyCache<string, SkinnedBodyMeasurement?> _bySet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when none of the meshes has vertices to measure.</summary>
    public SkinnedBodySize? Measure(IReadOnlyList<string> meshPaths)
    {
        var sorted = meshPaths.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var key = string.Join(MeshSeparator, sorted);
        return _bySet.GetOrCreate(key, () => MeasureSet(key, sorted))?.Size;
    }

    /// <summary>Every successful measurement so far, ordered by mesh set.</summary>
    public List<SkinnedBodyMeasurement> GetMeasurements() =>
        _bySet.GetCreatedValues()
            .OfType<SkinnedBodyMeasurement>()
            .OrderBy(measurement => measurement.Meshes, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private SkinnedBodyMeasurement? MeasureSet(string key, IReadOnlyList<string> meshPaths)
    {
        var vertices = meshPaths
            .Select(readGeometry)
            .OfType<NifGeometry>()
            .Select(geometry => geometry.Vertices)
            .ToList();
        return WaistBandSizing.Measure(vertices) is { } size ? new SkinnedBodyMeasurement(key, size) : null;
    }
}
