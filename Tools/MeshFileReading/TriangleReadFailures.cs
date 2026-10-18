using System.Collections.Concurrent;

namespace AddedObjectRemover;

/// <summary>Which meshes' triangle reads went fine and which failed in one run, by mesh path ignoring case. Thread-safe.</summary>
internal sealed class TriangleReadFailures
{
    private readonly ConcurrentDictionary<string, bool> _readFine = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MeshFailureCategory> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _failedAfterGoodRead = new(StringComparer.OrdinalIgnoreCase);

    public void NoteGoodRead(string meshPath) => _readFine.TryAdd(meshPath, true);

    /// <returns>Whether the mesh's triangles were read fine earlier in the run.</returns>
    public bool NoteFailure(string meshPath, MeshFailureCategory failure)
    {
        _failed.TryAdd(meshPath, failure);
        var readFineBefore = _readFine.ContainsKey(meshPath);
        if (readFineBefore) _failedAfterGoodRead.TryAdd(meshPath, true);
        return readFineBefore;
    }

    public TriangleFailureCounts Count() =>
        new([.. _failed.Values.CountBy(failure => failure)], _failedAfterGoodRead.Count);
}
