using System.Reflection;
using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools are generic. Their source (code and comments) and Tools/ToolConstants.txt hold none of the words of the steps, and
/// no public signature of a tool names a Mutagen type, except at the places where a tool meets Synthesis.
/// </summary>
public class ToolVocabularyTests
{
    private const string ToolsFolder = WordingTests.ToolsFolder;
    private const string ConstantsFile = "Tools/ToolConstants.txt";

    /// <summary>
    /// Words only a step uses, as regular expressions matched ignoring case: what the steps remove, restore or undo, the mods they
    /// work on, the steps themselves. Words with a neutral use in a tool (a grid step, removing from a collection) are not here.
    /// </summary>
    private static readonly string[] StepWords =
    [
        "removal",
        "restor",
        "undo",
        "sunk",
        "too close",
        "clashing",
        "waterfall",
        "keeper",
        "also remove",
        @"cleaned mods?\b",
        @"target mods?\b",
        @"other mods?\b",
        @"left.?behind",
        @"(report|pipeline) steps?\b",
        @"\bsteps?'s?\b",
        @"\bsteps? (run|decide|word)s?\b",
    ];

    private static readonly Regex StepWordPattern = new(string.Join('|', StepWords.Select(word => $"(?:{word})")), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] LibraryNamespaces = ["Mutagen.Bethesda", "Noggog"];

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The public types whose signatures name a Mutagen or Noggog type, each with why the tool has to.</summary>
    private static readonly Dictionary<string, string> SynthesisBoundary = new()
    {
        ["FinalWorldFactory"] = "Builds the final world from the Synthesis state's plugin listings and patch, and the library's directory path.",
        ["PluginOpener"] = "Opens plugins through Mutagen; the entry or a test passes the opener in, so it names the library's load order.",
        ["PluginListRead.Found"] = "Carries the library's load order listings read from the game's plugins file.",
        ["PluginListReader"] = "Reads the game's plugin list through Mutagen, from the Synthesis state's game release and plugin keys.",
        ["PatchWriting"] = "Writes into the patch mod Synthesis hands over.",
        ["SynthesisWriteCache"] = "Is the Synthesis state's link cache.",
        ["MeshFilesFactory"] = "Takes the Synthesis state's game release to find the game's archives.",
    };

    [Fact]
    public void TheToolsHoldNoWordOfTheSteps()
    {
        var uses = ToolTexts().SelectMany(text => StepWordUses(text.Path, text.Content)).Order(StringComparer.Ordinal).ToList();

        Assert.True(uses.Count == 0, $"Step words in a tool (a tool names what it does, not who asks):{Environment.NewLine}{string.Join(Environment.NewLine, uses)}");
    }

    [Fact]
    public void TheVocabularyScanReadsToolSourceAndTheConstantsList()
    {
        var paths = ToolTexts().Select(text => text.Path).ToList();

        Assert.Contains(ConstantsFile, paths);
        Assert.Contains($"{ToolsFolder}/BoxAndTransformMath/Box.cs", paths);
    }

    [Theory]
    [InlineData("the removal of an object", true)]
    [InlineData("Restored later", true)]
    [InlineData("an undo plan", true)]
    [InlineData("a sunk navmesh", true)]
    [InlineData("the cleaned mod", true)]
    [InlineData("the target mod", true)]
    [InlineData("other mods' objects", true)]
    [InlineData("the left-behind kinds", true)]
    [InlineData("a step's candidates", true)]
    [InlineData("the report step words it", true)]
    [InlineData("steps run work in parallel", true)]
    [InlineData("one grid step beyond the edge", false)]
    [InlineData("height steps of 8 units", false)]
    [InlineData("a step too steep to store", false)]
    [InlineData("_slots.Remove(key)", false)]
    [InlineData("other modules", false)]
    [InlineData("a target modifier", false)]
    public void TheStepWordPatternSeesStepWordsAndNotNeutralUses(string text, bool expected) =>
        Assert.Equal(expected, StepWordPattern.IsMatch(text));

    [Fact]
    public void NoToolSignatureNamesALibraryTypeExceptAtTheSynthesisBoundary()
    {
        var found = ToolAssemblies().SelectMany(assembly => assembly.GetExportedTypes()).Where(NamesLibraryType).Select(BusinessCode.PathName).ToHashSet();

        var notAllowed = found.Except(SynthesisBoundary.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = SynthesisBoundary.Keys.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"A public tool type names a Mutagen or Noggog type; keep the library inside the tool:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Listed in {nameof(SynthesisBoundary)} but no longer naming a library type in its public signatures (remove)");
    }

    [Fact]
    public void TheToolAssembliesAreFound()
    {
        var names = ToolAssemblies().Select(assembly => assembly.GetName().Name).ToList();

        Assert.Contains("PluginRecordReadingAndWriting", names);
        Assert.Contains("BoxAndTransformMath", names);
        Assert.DoesNotContain("AddedObjectRemover", names);
    }

    [Theory]
    [InlineData(typeof(SampleReturnsLibraryType), true)]
    [InlineData(typeof(SampleTakesLibraryTypeInAGeneric), true)]
    [InlineData(typeof(SampleHoldsLibraryTypeInAField), true)]
    [InlineData(typeof(SampleDerivesFromLibraryGeneric), true)]
    [InlineData(typeof(SampleKeepsLibraryTypeInside), false)]
    public void TheSignatureScanSeesLibraryTypesAnywhereInThePublicSurface(Type type, bool expected) =>
        Assert.Equal(expected, NamesLibraryType(type));

    private static IEnumerable<(string Path, string Content)> ToolTexts() =>
        SourceFile.All()
            .Where(file => file.IsIn([ToolsFolder]))
            .Select(file => (file.Path, File.ReadAllText(file.FullPath)))
            .Append((ConstantsFile, File.ReadAllText(Path.Combine(SolutionLayout.Root(), ConstantsFile))));

    private static IEnumerable<string> StepWordUses(string path, string content) =>
        StepWordPattern.Matches(content)
            .Select(match => $"{path}:{content.AsSpan(0, match.Index).Count('\n') + 1}: {match.Value}");

    private static IEnumerable<Assembly> ToolAssemblies()
    {
        var rules = ProjectRules.Load();
        var toolNames = rules.Projects.Where(rules.IsTool).Select(project => project.Name).ToHashSet();
        return ProductionSources.Assemblies().Where(assembly => toolNames.Contains(assembly.GetName().Name!));
    }

    /// <summary>The type's base type and interfaces, and the types in the signatures of its public and protected members.</summary>
    private static bool NamesLibraryType(Type type) => SurfaceTypes(type).Any(surface => BusinessCode.Mentions(surface, IsLibraryType));

    private static IEnumerable<Type> SurfaceTypes(Type type)
    {
        if (type.BaseType is { } baseType) yield return baseType;
        foreach (var implemented in type.GetInterfaces()) yield return implemented;
        foreach (var field in type.GetFields(Declared).Where(IsExposed)) yield return field.FieldType;
        foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)).Where(IsExposed))
        {
            if (method is MethodInfo { ReturnType: var returnType }) yield return returnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }
    }

    private static bool IsExposed(MethodBase method) =>
        (method.Attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private static bool IsExposed(FieldInfo field) =>
        (field.Attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;

    private static bool IsLibraryType(Type type) =>
        type.Namespace is { } name
        && LibraryNamespaces.Any(library => name == library || name.StartsWith(library + ".", StringComparison.Ordinal));
}

internal sealed class SampleReturnsLibraryType
{
    public Mutagen.Bethesda.Plugins.ModKey Key() => default;
}

internal sealed class SampleTakesLibraryTypeInAGeneric
{
    public void Take(Dictionary<int, Mutagen.Bethesda.Plugins.ModKey[]> keys) => _ = keys;
}

internal sealed class SampleHoldsLibraryTypeInAField
{
    public readonly Mutagen.Bethesda.Plugins.ModKey? Key = null;
}

internal sealed class SampleDerivesFromLibraryGeneric : List<Mutagen.Bethesda.Plugins.ModKey>;

internal sealed class SampleKeepsLibraryTypeInside
{
    public int Count => 0;

    internal Mutagen.Bethesda.Plugins.ModKey Hidden() => default;
}
