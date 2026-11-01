using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>A base record the game library cannot parse fails as malformed user data, which a run records and carries on from; our own errors do not.</summary>
public class MalformedBaseRecordTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;
    private readonly SkyrimMod _mod = new(ModKey.FromNameAndExtension("Bases.esp"), SkyrimRelease.SkyrimSE);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(BrokenScriptData.Fault.EndsInsideTheScriptName, typeof(IndexOutOfRangeException))]
    [InlineData(BrokenScriptData.Fault.EndsInsideTheLastPropertyValue, typeof(ArgumentOutOfRangeException))]
    [InlineData(BrokenScriptData.Fault.UnknownPropertyType, typeof(NotImplementedException))]
    public void ABaseWhoseScriptDataTheLibraryCannotParseFailsAsMalformedUserData(BrokenScriptData.Fault fault, Type libraryException)
    {
        var quest = _mod.Quests.AddNew("NamedQuest");
        var activator = _mod.Activators.AddNew("Scripted");
        activator.VirtualMachineAdapter = TestScripts.Naming(quest.FormKey);
        using var plugin = BrokenScriptData.WriteAndOpen(_mod, _folder, fault);
        var reader = TestBaseFacts.Reader(plugin.ToImmutableLinkCache());

        var failure = Assert.Throws<MalformedRecordException>(() => reader.Of(new BaseKey(activator.FormKey.ToRecordKey(), BaseLinkKind.PlaceableObject)));

        Assert.IsType(libraryException, failure.InnerException);
        Assert.True(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void TheSameExceptionTypesThrownByOurOwnCodeInsideALibraryReadStayBugs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MutagenCalls.Read(() => new List<int>()[0]));
        Assert.Throws<IndexOutOfRangeException>(() => MutagenCalls.Read(() => Array.Empty<int>()[0]));
        Assert.Throws<NotImplementedException>(() => MutagenCalls.Read<int>(() => throw new NotImplementedException()));
    }
}
