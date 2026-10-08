using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestKeepReasons
{
    /// <summary>A target object kept because a quest links to it.</summary>
    public static readonly KeepReason Quest = NonPlaced("QUST", "MyQuest");

    public const string QuestDetail = "linked from QUST MyQuest [000001:Quest.esp]";

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
                SourceIsWorldspace: false));
}
