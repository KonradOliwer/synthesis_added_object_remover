namespace AddedObjectRemover.Tests.LogTextFormatting;

public class RecordLabelsTests
{
    private static readonly RecordKey Key = new(new PluginName("Example.esp"), 0xABCD);

    [Fact]
    public void OfWritesTheEditorIdBeforeTheKey() =>
        Assert.Equal("MyChest [00ABCD:Example.esp]", RecordLabels.Of(Key, "MyChest"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OfWritesJustTheKeyWithoutAnEditorId(string? editorId) =>
        Assert.Equal("00ABCD:Example.esp", RecordLabels.Of(Key, editorId));
}
