namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>Which target objects the changed-terrain rule checks; objects in interiors or in worldspaces without terrain never are.</summary>
public enum TerrainCheckedObjects
{
    /// <summary>Visible objects whose box, grown by the terrain margin, reaches the terrain surface as it was or as it is now at a checked vertex.</summary>
    VisibleObjectsOnTheGround,

    /// <summary>Every visible object, at any height.</summary>
    VisibleObjectsAtAnyHeight,

    /// <summary>Every object, at any height, invisible ones included.</summary>
    AllObjectsIncludingInvisibleOnes,
}
