using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

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
    TooCloseOptions Options,
    TooCloseResult Result,
    InvisibleOtherObjectCounts Census,
    ImmutableArray<KeptObject> Kept,
    PhaseProblems Problems,
    int MeshesIndexed,
    long MeshTriangles,
    int BodiesBuilt,
    int NpcBasesResolved,
    int LeveledListsResolved,
    int BodyMeshSetsMeasured,
    int EffectOnlyMeshes,
    TimeSpan Elapsed);
