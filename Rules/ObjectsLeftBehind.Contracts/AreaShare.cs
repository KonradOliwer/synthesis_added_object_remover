namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>How much of the cleaned mod's objects still stand in an area.</summary>
public enum AreaVerdict
{
    /// <summary>The area holds none of the cleaned mod's drawn objects.</summary>
    NoObjectsAround,

    /// <summary>At most the allowed share still stands.</summary>
    Cleared,

    /// <summary>More than the allowed share still stands.</summary>
    StillStanding,
}

/// <summary>The cleaned mod's drawn objects in an area and how many of them still stand, by count and by size.</summary>
/// <param name="Objects">The objects counted, effect-only ones included.</param>
/// <param name="Standing">Those not removed.</param>
/// <param name="Size">The box volume of the objects counted, in cubic game units; effect-only objects add nothing.</param>
/// <param name="StandingSize">The box volume of those not removed.</param>
/// <param name="CountPercent">The percent of the objects that still stand; 0 when the area holds no object.</param>
/// <param name="SizePercent">The percent of the size that still stands; 0 when the objects have no volume.</param>
public sealed record AreaShare(int Objects, int Standing, double Size, double StandingSize, AreaVerdict Verdict, double CountPercent, double SizePercent);
