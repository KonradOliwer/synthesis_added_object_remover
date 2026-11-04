using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>How the also-remove rounds open, whichever step decides them.</summary>
public static class AlsoRemoveRounds
{
    /// <summary>What goes with a removed object goes, also an object kept shrunk or turned where it stands, whose fit is then replaced.</summary>
    public static readonly RoundOpening Opening = new(RoundKind.AlsoRemove, MayReplaceFits: true);
}
