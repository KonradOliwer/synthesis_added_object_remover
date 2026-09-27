namespace AddedObjectRemover;

/// <summary>Where an NPC's body size came from, best first; <see cref="Point"/> is the point-detection fallback.</summary>
internal enum NpcSizeSource
{
    BodyMesh,
    ObjectBounds,
    HumanoidApproximation,
    Point,
}

/// <summary>
/// The body of an NPC as an upright box standing on the placement point, in NPC-local units at
/// reference scale 1 (the race and NPC height are already applied). A <see cref="NpcSizeSource.Point"/>
/// body is a zero-size box at the placement point, so it is stuck only when its feet are deeper
/// in the target than <see cref="NpcStuckTest.FootClearance"/>.
/// </summary>
/// <param name="PointReason">Why the real size could not be determined; set only for <see cref="NpcSizeSource.Point"/>.</param>
internal sealed record NpcBody(NpcSizeSource Source, Box LocalBox, string? PointReason)
{
    public static NpcBody FromBox(NpcSizeSource source, Box localBox, float heightScale) =>
        new(source, localBox.Scaled(heightScale), null);

    public static NpcBody Point(string reason) => new(NpcSizeSource.Point, Box.Zero, reason);
}

/// <summary>
/// Every distinct body a placed base can have: one for a plain NPC, one per distinct possible NPC
/// of a leveled list or template chain, in record order.
/// </summary>
/// <param name="CombinedBox">Encloses every body: the largest width, depth and height of them all.</param>
internal sealed record NpcBodySet(IReadOnlyList<NpcBody> Bodies, Box CombinedBox)
{
    /// <summary>Bodies with the same box are tested once; the first of them is kept.</summary>
    public static NpcBodySet Of(IReadOnlyList<NpcBody> possibleBodies)
    {
        var distinct = possibleBodies.DistinctBy(body => body.LocalBox).ToList();
        return new NpcBodySet(distinct, distinct.Skip(1).Aggregate(distinct[0].LocalBox, (union, body) => union.Union(body.LocalBox)));
    }

    /// <summary>The least precise size source among the bodies.</summary>
    public NpcSizeSource Source => Bodies.Max(body => body.Source);

    /// <summary>The first body sized as a point, if any.</summary>
    public NpcBody? FirstPoint => Bodies.FirstOrDefault(body => body.Source == NpcSizeSource.Point);
}
