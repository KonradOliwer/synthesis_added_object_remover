using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

public enum SupporterType { Target, PlacedObject, Terrain }

public readonly record struct Supporter
{
    private const int NoIndex = -1;

    private Supporter(SupporterType type, int index)
    {
        Type = type;
        Index = index;
    }

    public SupporterType Type { get; }

    /// <summary>The <see cref="TargetId"/> index of a target; the <see cref="OtherId"/> index of a placed object.</summary>
    public int Index { get; }

    public static Supporter Terrain { get; } = new(SupporterType.Terrain, NoIndex);

    public static Supporter Target(int targetIndex) => new(SupporterType.Target, targetIndex);

    public static Supporter Placed(OtherId id) => new(SupporterType.PlacedObject, id.Index);
}

public readonly record struct SupporterWeight(Supporter Supporter, float Weight);

/// <param name="ContactPoints">Surface samples in contact with at least one supporter.</param>
/// <param name="TotalWeight">Sum of the weights of all contact points.</param>
/// <param name="Supporters">Weight held by each supporter with at least one contact point, in a fixed order.</param>
public sealed record CandidateContacts(int ContactPoints, float TotalWeight, IReadOnlyList<SupporterWeight> Supporters);

public readonly record struct MeshSupporter(Supporter Supporter, PlacedTransform Transform, string MeshPath);
