namespace AddedObjectRemover.Tests.Architecture;

public class ProjectRulesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The number of entries of <see cref="ProjectRules.StepResultReferences"/> today.</summary>
    private const int MostStepResultReferences = 117;

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
    public void RulesReferenceOnlyToolsIdsCachesRulesAndTheWorldData() =>
        Assert.Empty(Rules.FindRulesReferencingOtherThanToolsIdsCachesRulesAndTheWorldData());

    [Fact]
    public void ARuleReferenceToAnythingButToolsIdsCachesRulesAndTheWorldDataIsReported()
    {
        var bad = Table(
            new ProjectRule("Rule", "U", "Rules/Rule", ["Rule.Contracts", "Other.Rule.Contracts", "Tool", "Ids", "RunCaches.Contracts", "CollectPlacedObjects.Contracts", "Step.Contracts", "Step"], []),
            new ProjectRule("Rule.Contracts", "C", "Rules/Rule.Contracts", ["Step.Contracts", "RemovalDecisionList.Contracts"], []),
            new ProjectRule("Other.Rule.Contracts", "C", "Rules/Other.Rule.Contracts", [], []),
            new ProjectRule("Tool", "T", "Tools/Tool", [], []),
            new ProjectRule("Ids", "K", "Ids", [], []),
            new ProjectRule("RunCaches.Contracts", "Q", "Caches/RunCaches.Contracts", [], []),
            new ProjectRule("CollectPlacedObjects.Contracts", "C", "Steps/CollectPlacedObjects.Contracts", [], []),
            new ProjectRule("RemovalDecisionList.Contracts", "C", "Steps/RemovalDecisionList.Contracts", [], []),
            new ProjectRule("Step.Contracts", "C", "Steps/Step.Contracts", [], []),
            new ProjectRule("Step", "L", "Steps/Step", [], []));

        Assert.Equal(["Rule -> Step.Contracts", "Rule -> Step", "Rule.Contracts -> Step.Contracts"], bad.FindRulesReferencingOtherThanToolsIdsCachesRulesAndTheWorldData());
    }

    [Fact]
    public void StepsReferenceOtherStepsOnlyForTheirResults()
    {
        Assert.Empty(Rules.FindStepsReferencingOtherStepsWithoutReason());
        Assert.Empty(Rules.FindListedStepResultReferencesThatDoNotExist());
    }

    /// <summary>
    /// The list only shrinks: a rule two steps share belongs in a rule module, not in a new entry. Each change that removes entries
    /// lowers <see cref="MostStepResultReferences"/> to the new count.
    /// </summary>
    [Fact]
    public void TheStepResultReferencesNeverGrow() =>
        Assert.True(
            ProjectRules.StepResultReferences.Count <= MostStepResultReferences,
            $"{ProjectRules.StepResultReferences.Count} step result references; at most {MostStepResultReferences} are allowed.");

    [Fact]
    public void AStepReferenceToAnotherStepsContractsWithoutAListedReasonIsReported()
    {
        var bad = Table(
            new ProjectRule("Step", "L", "Steps/Step", ["Step.Contracts", "Other.Contracts", "Rule.Contracts"], []),
            new ProjectRule("Step.Contracts", "C", "Steps/Step.Contracts", ["Other.Contracts"], []),
            new ProjectRule("Other.Contracts", "C", "Steps/Other.Contracts", [], []),
            new ProjectRule("Rule.Contracts", "C", "Rules/Rule.Contracts", [], []));

        Assert.Equal(["Step -> Other.Contracts", "Step.Contracts -> Other.Contracts"], bad.FindStepsReferencingOtherStepsWithoutReason());
    }

    [Fact]
    public void AListedStepResultReferenceThatIsGoneIsReported()
    {
        var listed = ProjectRules.StepResultReferences.Keys.First();
        var projectName = listed[..listed.IndexOf(' ')];
        var bad = Table(new ProjectRule(projectName, "L", $"Steps/{projectName}", [], []));

        Assert.Contains(listed, bad.FindListedStepResultReferencesThatDoNotExist());
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
    public void EveryRowsKindFitsItsFolderAndName() =>
        Assert.Empty(Rules.FindRowsWhoseKindDoesNotFitFolderAndName());

    [Fact]
    public void ARowWhoseKindDoesNotFitItsFolderAndNameIsReported()
    {
        var table = Table(
            new ProjectRule("Good", "L", "Steps/Good", [], []),
            new ProjectRule("Good.Contracts", "C", "Steps/Good.Contracts", [], []),
            new ProjectRule("Marked.Contracts", "L", "Steps/Marked.Contracts", [], []),
            new ProjectRule("Marked", "C", "Steps/Marked", [], []),
            new ProjectRule("Rule", "U", "Rules/Rule", [], []),
            new ProjectRule("Rule.Contracts", "C", "Rules/Rule.Contracts", [], []),
            new ProjectRule("StepLikeRule", "L", "Rules/StepLikeRule", [], []),
            new ProjectRule("RuleLike.Contracts", "U", "Rules/RuleLike.Contracts", [], []),
            new ProjectRule("Cache", "L", "Caches/Cache", [], []),
            new ProjectRule("Catalog.Contracts", "Q", "Caches/Catalog.Contracts", [], []),
            new ProjectRule("Other.Contracts", "V", "Caches/Other.Contracts", [], []),
            new ProjectRule("Tool", "T", "Tools/Tool", [], []),
            new ProjectRule("Facts", "A", "Tools/Facts", [], []),
            new ProjectRule("Misplaced", "L", "Tools/Misplaced", [], []),
            new ProjectRule("Run", "R", "Run/Run", [], []),
            new ProjectRule("Settings", "V", "Run/Settings", [], []),
            new ProjectRule("Settings.Contracts", "R", "Run/Settings.Contracts", [], []),
            new ProjectRule("Ids", "K", "Ids", [], []),
            new ProjectRule("Entry", "E", "Entry", [], []),
            new ProjectRule("Entry.Tests", "X", "Entry.Tests", [], []),
            new ProjectRule("Entry.Other.Tests", "E", "Entry.Other.Tests", [], []),
            new ProjectRule("Odd", "T", "Elsewhere/Odd", [], []));

        Assert.Equal(
            [
                "Marked.Contracts (Steps/Marked.Contracts) is kind L; its folder and name allow only C",
                "Marked (Steps/Marked) is kind C; its folder and name allow only L",
                "StepLikeRule (Rules/StepLikeRule) is kind L; its folder and name allow only U",
                "RuleLike.Contracts (Rules/RuleLike.Contracts) is kind U; its folder and name allow only C",
                "Cache (Caches/Cache) is kind L; its folder and name allow only B",
                "Other.Contracts (Caches/Other.Contracts) is kind V; its folder and name allow only C or Q",
                "Misplaced (Tools/Misplaced) is kind L; its folder and name allow only A or T",
                "Settings.Contracts (Run/Settings.Contracts) is kind R; its folder and name allow only C",
                "Entry.Other.Tests (Entry.Other.Tests) is kind E; its folder and name allow only X",
                "Odd (Elsewhere/Odd) is kind T; its folder and name allow no kind",
            ],
            table.FindRowsWhoseKindDoesNotFitFolderAndName());
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
