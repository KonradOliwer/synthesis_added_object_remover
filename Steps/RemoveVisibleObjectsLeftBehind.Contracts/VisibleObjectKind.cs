namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>Kinds of visible placed objects that can be kept as a whole, by their base record type; every other visible object is judged like any other.</summary>
public enum VisibleObjectKind
{
    NpcsAndCreatures,
    Containers,
    Furniture,
    Activators,
}
