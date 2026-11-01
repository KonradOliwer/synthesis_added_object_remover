using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestKeepReasons
{
    /// <summary>A target object kept because a quest links to it.</summary>
    public static readonly KeepReason Quest = NonPlaced("QUST", "MyQuest");

    public const string QuestDetail = "linked from QUST MyQuest [000001:Quest.esp]";

    /// <summary>A cell of the base game that CityMod.esp deletes.</summary>
    public static readonly DeletedCell DeletedByCityMod = new(
        new CellFact(new RecordKey(new PluginName("Skyrim.esm"), 0x000D00), "DeletedCell"), new PluginName("CityMod.esp"));

    /// <summary>A target object kept because the cell it stands in is deleted.</summary>
    public static readonly KeepReason InDeletedCell = new(KeepKind.CellDeleted, CellDeleted: DeletedByCityMod);

    public static KeepReason NonPlaced(string recordType, string editorId) =>
        new(
            KeepKind.NonPlacedReference,
            new LinkFact(
                new FormKey(ModKey.FromNameAndExtension("Quest.esp"), 0x1).ToRecordKey(),
                editorId,
                recordType,
                TestTargets.Key(0),
                RelationKind.Other,
                SourceIsPlaced: false,
                SourceIsWorldspace: false,
                AliasFillFlags.None));
}
