namespace AddedObjectRemover;

/// <param name="Meshes">The measured meshes, joined in path order.</param>
/// <param name="Bounds">The meshes' combined render-geometry bounds.</param>
internal sealed record SkinnedBodyMeasurement(string Meshes, Box Bounds);

/// <summary>
/// Thread-safe cache of the combined bounds per set of body meshes (the addons one body armour
/// gives one race and sex). Each set is read and measured once; failures are cached too.
/// </summary>
internal sealed class SkinnedBodyMeasurer(Func<string, NifGeometry?> readGeometry)
{
    private const string MeshSeparator = " + ";

    private readonly LazyCache<string, SkinnedBodyMeasurement?> _bySet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when none of the meshes can be read or their bounds have no height.</summary>
    public Box? Measure(IReadOnlyList<string> meshPaths)
    {
        var sorted = meshPaths.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var key = string.Join(MeshSeparator, sorted);
        return _bySet.GetOrCreate(key, () => MeasureSet(key, sorted))?.Bounds;
    }

    /// <summary>Every successful measurement so far, ordered by mesh set.</summary>
    public List<SkinnedBodyMeasurement> GetMeasurements() =>
        _bySet.GetCreatedValues()
            .OfType<SkinnedBodyMeasurement>()
            .OrderBy(measurement => measurement.Meshes, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private SkinnedBodyMeasurement? MeasureSet(string key, IReadOnlyList<string> meshPaths)
    {
        Box? bounds = null;
        foreach (var geometry in meshPaths.Select(readGeometry).OfType<NifGeometry>())
        {
            var meshBounds = new Box(geometry.Min, geometry.Max);
            bounds = bounds is { } found ? found.Union(meshBounds) : meshBounds;
        }
        return bounds is { Size.Z: > 0 } measured ? new SkinnedBodyMeasurement(key, measured) : null;
    }
}
