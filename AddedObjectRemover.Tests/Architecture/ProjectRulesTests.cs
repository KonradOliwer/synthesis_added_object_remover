namespace AddedObjectRemover.Tests.Architecture;

public class ProjectRulesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    [Fact]
    public void TheTableHasNoDuplicateNamesAndOnlyKnownReferences()
    {
        var names = Rules.Projects.Select(project => project.Name).ToList();

        Assert.Equal(names.Distinct(), names);
        Assert.Empty(Rules.Projects.SelectMany(project => project.References.Except(names).Select(reference => $"{project.Name} -> {reference}")));
    }

    [Fact]
    public void LogicProjectsReferenceNoLogic() =>
        Assert.Empty(Rules.FindLogicReferencingLogic());

    [Fact]
    public void OnlyTheRunnerAndTheTestsReferenceLogic() =>
        Assert.Empty(Rules.FindLogicReferencedByNonRunners());

    [Fact]
    public void OnlyTheEntryAndTheTestsReferenceTheEntryOnlyProjects() =>
        Assert.Empty(Rules.FindEntryOnlyReferencedByOthers());

    [Fact]
    public void NoStepContractsReferenceTheRunCachesContracts() =>
        Assert.Empty(Rules.FindStepContractsReferencingRunCachesContracts());

    [Fact]
    public void StepContractsReferencingTheRunCachesContractsAreReported()
    {
        var bad = Table(
            new ProjectRule("Step.Contracts", "C", "Steps/Step.Contracts", ["RunCaches.Contracts"], []),
            new ProjectRule("Other.Contracts", "C", "Run/Other.Contracts", ["RunCaches.Contracts"], []),
            new ProjectRule("RunCaches.Contracts", "Q", "Caches/RunCaches.Contracts", [], []));

        Assert.Equal(["Step.Contracts -> RunCaches.Contracts"], bad.FindStepContractsReferencingRunCachesContracts());
    }

    [Fact]
    public void NoStepContractsReferenceTheRunsContracts() =>
        Assert.Empty(Rules.FindStepContractsReferencingRunContracts());

    [Fact]
    public void StepContractsReferencingTheRunsContractsAreReported()
    {
        var bad = Table(
            new ProjectRule("Step.Contracts", "C", "Steps/Step.Contracts", ["Run.Contracts", "Other.Step.Contracts"], []),
            new ProjectRule("Other.Step.Contracts", "C", "Steps/Other.Step.Contracts", [], []),
            new ProjectRule("Cache.Contracts", "C", "Caches/Cache.Contracts", ["Run.Contracts"], []),
            new ProjectRule("Run.Contracts", "C", "Run/Run.Contracts", [], []));

        Assert.Equal(["Step.Contracts -> Run.Contracts"], bad.FindStepContractsReferencingRunContracts());
    }

    [Fact]
    public void LogicReferencesTheRunsContractsOnlyWhereARealReasonIsListed()
    {
        Assert.Empty(Rules.FindLogicReferencingRunContractsWithoutReason());
        Assert.Empty(Rules.FindListedLogicReferencesToRunContractsThatDoNotExist());
    }

    [Fact]
    public void ALogicReferenceToTheRunsContractsWithoutAListedReasonIsReported()
    {
        var bad = Table(
            new ProjectRule("BuildLogAndReports", "L", "Steps/BuildLogAndReports", ["RunSettingsValidation.Contracts", "RunAllSteps.Contracts"], []),
            new ProjectRule("Cache", "B", "Caches/Cache", ["RunSettingsValidation.Contracts"], []),
            new ProjectRule("RunSettingsValidation.Contracts", "C", "Run/RunSettingsValidation.Contracts", [], []),
            new ProjectRule("RunAllSteps.Contracts", "C", "Run/RunAllSteps.Contracts", [], []));

        Assert.Equal(["BuildLogAndReports -> RunAllSteps.Contracts", "Cache -> RunSettingsValidation.Contracts"], bad.FindLogicReferencingRunContractsWithoutReason());
    }

    [Fact]
    public void AListedLogicReferenceToTheRunsContractsThatIsGoneIsReported()
    {
        var bad = Table(
            new ProjectRule("BuildLogAndReports", "L", "Steps/BuildLogAndReports", [], []),
            new ProjectRule("RunSettingsValidation.Contracts", "C", "Run/RunSettingsValidation.Contracts", [], []));

        Assert.Equal(["BuildLogAndReports -> RunSettingsValidation.Contracts"], bad.FindListedLogicReferencesToRunContractsThatDoNotExist());
    }

    [Fact]
    public void ToolsReferenceOnlyToolsAndIds() =>
        Assert.Empty(Rules.FindToolsReferencingNonTools());

    [Fact]
    public void ContractsReferenceOnlyContractsToolsAndIds() =>
        Assert.Empty(Rules.FindContractsReferencingNonContractsAndNonTools());

    [Fact]
    public void CacheContractsReferenceNoRunContracts() =>
        Assert.Empty(Rules.FindCacheContractsReferencingRunContracts());

    [Fact]
    public void ToolsReferencingAnythingButToolsAndIdsAreReported()
    {
        var bad = Table(
            Project("Facts", "A", "Reading", "Contracts", "Ids", "Logic"),
            Project("Reading", "T"),
            Project("Contracts", "C"),
            Project("Ids", "K"),
            Project("Logic", "L"));

        Assert.Equal(["Facts -> Contracts", "Facts -> Logic"], bad.FindToolsReferencingNonTools());
    }

    [Fact]
    public void ContractsReferencingAnythingButContractsToolsAndIdsAreReported()
    {
        var bad = Table(
            Project("Step.Contracts", "C", "Other.Contracts", "Catalog", "Tool", "Ids", "Logic", "Runner"),
            Project("Catalog", "Q", "Logic"),
            Project("Other.Contracts", "C"),
            Project("Tool", "T"),
            Project("Ids", "K"),
            Project("Logic", "L"),
            Project("Runner", "R"));

        Assert.Equal(["Step.Contracts -> Logic", "Step.Contracts -> Runner", "Catalog -> Logic"], bad.FindContractsReferencingNonContractsAndNonTools());
    }

    [Fact]
    public void CacheContractsReferencingRunContractsAreReported()
    {
        var bad = Table(
            new ProjectRule("Cache.Contracts", "C", "Caches/Cache.Contracts", ["Step.Contracts", "Other.Cache.Contracts", "Run.Contracts"], []),
            new ProjectRule("Catalog", "Q", "Caches/Catalog", ["Run.Contracts"], []),
            new ProjectRule("Step.Contracts", "C", "Steps/Step.Contracts", ["Run.Contracts"], []),
            new ProjectRule("Other.Cache.Contracts", "C", "Caches/Other.Cache.Contracts", [], []),
            new ProjectRule("Run.Contracts", "C", "Run/Run.Contracts", [], []));

        Assert.Equal(["Cache.Contracts -> Run.Contracts", "Catalog -> Run.Contracts"], bad.FindCacheContractsReferencingRunContracts());
    }

    [Fact]
    public void TheReferencesHaveNoCycle() =>
        Assert.Empty(Rules.FindCycles());

    [Fact]
    public void ALogicProjectReferencingLogicIsReported()
    {
        var bad = Table(Project("A", "L", "B"), Project("B", "L"));

        Assert.Equal(["A -> B"], bad.FindLogicReferencingLogic());
    }

    [Fact]
    public void ALogicProjectReferencedByAnotherLogicProjectIsReported()
    {
        var bad = Table(Project("A", "L", "B"), Project("B", "B"), Project("Run", "R", "B"));

        Assert.Equal(["A -> B"], bad.FindLogicReferencedByNonRunners());
    }

    [Fact]
    public void AnEntryOnlyProjectReferencedBySomeoneElseIsReported()
    {
        var bad = Table(Project("Entry", "E", "Checks"), Project("Test", "X", "Checks"), Project("Run", "R", "Checks"), Project("Checks", "V"));

        Assert.Equal(["Run -> Checks"], bad.FindEntryOnlyReferencedByOthers());
    }

    [Fact]
    public void ACycleIsReportedOnce()
    {
        var bad = Table(Project("A", "R", "B"), Project("B", "R", "A"));

        Assert.Equal(["A -> B -> A"], bad.FindCycles());
    }

    private static ProjectRules Table(params ProjectRule[] projects) => new([], projects);

    private static ProjectRule Project(string name, string kind, params string[] references) => new(name, kind, name, references, []);
}
