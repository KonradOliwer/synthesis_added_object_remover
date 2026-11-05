using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Rules;

public sealed class ExampleModTests
{
    private const string ExampleGroupName = "cleaned_mods";

    [Fact]
    public void TheExampleModLoadsBothConfigsInOrderListingEverySettingWithoutUnknownKeys()
    {
        var settings = new Settings { Mode = PatchMode.Batch, Batch = new BatchSettings { GroupFileName = ExampleGroupName } };

        var read = Program.ReadConfigs(settings, ExampleModFolder());

        var configs = Assert.IsType<ConfigsRead.Ready>(read).Configs;
        Assert.Equal(["SomeMod", "OtherMod"], configs.Select(config => Assert.IsType<SettingsSource.ConfigFile>(config.Source).Name));
        Assert.Equal(["SomeMod.esp", "OtherMod.esp"], configs.Select(config => config.Settings.CleanedMod?.ToString()));
        Assert.All(configs, config => Assert.Empty(Assert.IsType<SettingsSource.ConfigFile>(config.Source).Notes.UnknownKeys));
        Assert.All(configs, config => Assert.Empty(Assert.IsType<SettingsSource.ConfigFile>(config.Source).Notes.NotInFile));
    }

    /// <summary>This file sits in AddedObjectRemover.Tests\Rules.</summary>
    private static string ExampleModFolder([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "Example mod"));
}
