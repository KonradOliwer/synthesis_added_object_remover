namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

/// <param name="InWorld">Decides whether another mod's placed record is changed in the accepted world the run judges in.</param>
/// <param name="InEarlierWorlds">
/// Decides the same in the worlds without and before the cleaned mod. It must count no part an earlier run gave back: such a part
/// is part of the world as the patch now holds it, never of the world without or before this run's cleaned mod, whose own value
/// it may be.
/// </param>
public sealed record RecordChangeRules(IOtherRecordChange InWorld, IOtherRecordChange InEarlierWorlds);
