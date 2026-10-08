using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PluginNameParserTests
{
    [Theory]
    [InlineData("Example.esp", "Example.esp")]
    [InlineData("Example.ESP", "Example.esp")]
    [InlineData("My Mod.esm", "My Mod.esm")]
    [InlineData("Light.esl", "Light.esl")]
    public void AValidNameKeepsItsNameAndGetsALowerCaseExtension(string text, string expected)
    {
        Assert.True(PluginNameParser.TryParsePluginName(text, out var name));
        Assert.Equal(expected, name.FileName);
    }

    [Theory]
    [InlineData("NoExtension")]
    [InlineData("Example.txt")]
    [InlineData("")]
    public void AnInvalidNameIsRejected(string text)
    {
        Assert.False(PluginNameParser.TryParsePluginName(text, out _));
    }

    [Fact]
    public void TheParsedNameIsTheSameAsMutagensForTheSameText()
    {
        Assert.True(PluginNameParser.TryParsePluginName("Mixed.Case.ESM", out var name));
        Assert.Equal(ModKey.FromNameAndExtension("Mixed.Case.ESM").ToPluginName(), name);
    }
}
