namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <param name="Evaluated">Placed NPCs with a body, counted by their least precise possible body.</param>
/// <param name="WithoutNpc">Placed NPCs skipped because their base resolves to no NPC, so they never spawn.</param>
public readonly record struct NpcSizeCounts(
    int Evaluated,
    int ByBodyMesh,
    int ByObjectBounds,
    int ByHumanoidApproximation,
    int ByPoint,
    int WithoutNpc)
{
    public static NpcSizeCounts Of(IReadOnlyCollection<NpcBodySet> bodies, int withoutNpc) => new(
        bodies.Count,
        bodies.Count(body => body.Source == NpcSizeSource.BodyMesh),
        bodies.Count(body => body.Source == NpcSizeSource.ObjectBounds),
        bodies.Count(body => body.Source == NpcSizeSource.HumanoidApproximation),
        bodies.Count(body => body.Source == NpcSizeSource.Point),
        withoutNpc);

    public NpcSizeCounts Add(NpcSizeCounts other) => new(
        Evaluated + other.Evaluated,
        ByBodyMesh + other.ByBodyMesh,
        ByObjectBounds + other.ByObjectBounds,
        ByHumanoidApproximation + other.ByHumanoidApproximation,
        ByPoint + other.ByPoint,
        WithoutNpc + other.WithoutNpc);
}
