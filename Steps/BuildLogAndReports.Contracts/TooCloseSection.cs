using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>One performed pass of the too-close step, as the section words it.</summary>
/// <param name="AlreadyRemoved">The found objects that the changed-terrain round had removed before this round; they count under that step.</param>
/// <param name="Restored">The found objects that are restored.</param>
public sealed record TooClosePassReport(TooClosePass Pass, int AlreadyRemoved, int Restored);

/// <param name="Passes">The performed passes, one for each settings set, in order.</param>
/// <param name="Kept">The objects the too-close round left standing, in round order.</param>
/// <param name="MeshesIndexed">Meshes held when the phase ended.</param>
/// <param name="MeshTriangles">Triangles in those meshes.</param>
/// <param name="BodiesBuilt">NPC bodies built when the phase ended.</param>
/// <param name="NpcBasesResolved">NPC bases resolved when the phase ended.</param>
/// <param name="LeveledListsResolved">Leveled lists resolved when the phase ended.</param>
/// <param name="BodyMeshSetsMeasured">Distinct sets of body meshes whose bounds were measured.</param>
/// <param name="EffectOnlyMeshes">Meshes of fog, light rays and the like, which never make anything too close.</param>
public sealed record TooCloseSection(
    CollectedObjects World,
    PluginName Target,
    ImmutableArray<TooClosePassReport> Passes,
    InvisibleOtherObjectCounts Census,
    ImmutableArray<KeptObject> Kept,
    int MeshesIndexed,
    long MeshTriangles,
    int BodiesBuilt,
    int NpcBasesResolved,
    int LeveledListsResolved,
    int BodyMeshSetsMeasured,
    int EffectOnlyMeshes,
    TimeSpan Elapsed);
