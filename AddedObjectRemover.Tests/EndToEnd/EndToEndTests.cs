namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Console output is process-wide, so the end-to-end runs must not overlap other tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleOutputCollection
{
    public const string Name = "Console output";
}

/// <summary>Pins the whole run: the log (in order), the patch overrides and the report files.</summary>
[Collection(ConsoleOutputCollection.Name)]
public class EndToEndTests
{
    private const int OneWorker = 1;
    private const int ManyWorkers = 8;

    public static TheoryData<string> Variants => new(SettingsVariants.Names);

    [Theory]
    [MemberData(nameof(Variants))]
    public void OutputMatchesExpectedOutput(string variant)
    {
        var output = PipelineRun.Execute(SettingsVariants.Of(variant), ManyWorkers);

        ExpectedOutputFiles.AssertMatches(variant, output.ToExpectedOutputLines());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void OneAndManyWorkersGiveTheSameOutput(string variant)
    {
        var settings = SettingsVariants.Of(variant);

        var one = PipelineRun.Execute(settings, OneWorker).ToExpectedOutputLines();
        var many = PipelineRun.Execute(settings, ManyWorkers).ToExpectedOutputLines();

        Assert.Equal(one, many);
    }
}
