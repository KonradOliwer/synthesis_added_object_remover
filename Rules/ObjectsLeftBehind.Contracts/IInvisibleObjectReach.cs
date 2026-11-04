using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>How far an invisible object acts, when its record says so. Thread-safe.</summary>
public interface IInvisibleObjectReach
{
    /// <summary>How far a step looks around the target: its reach when shorter than the look-around distance.</summary>
    float RadiusAround(TargetObject target, float lookAround);
}
