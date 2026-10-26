using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Rules over the built types of all production projects: type names are unique, and the contracts projects hold
/// only public data shapes.
/// </summary>
public class ProductionTypeTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private const string ContractsSuffix = ".Contracts";
    private const string GetterPrefix = "get_";

    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The members the compiler writes for records and readonly record structs.</summary>
    private static readonly string[] RecordSynthesisedMethods = ["Equals", "GetHashCode", "ToString", "PrintMembers", "<Clone>$", "Deconstruct"];

    /// <summary>Contract types that are not a record, interface, enum or readonly struct, each with why it is allowed.</summary>
    private static readonly Dictionary<string, string> AllowedOtherTypes = new()
    {
        ["MeshLimits"] = "named constants: the mesh size limits",
        ["MarkerMoveLimits"] = "named constants: the marker move limit",
        ["OtherModsObjectsLimits"] = "named constants: how far another mod's object is pulled down",
        ["OtherModsObjectsParts"] = "named constants: the part keys the step for other mods' objects records its unexpected errors under",
        ["RemovalConvention"] = "named constants: the game's convention for a placed object switched off for good",
        ["ReportFileNames"] = "named constants: the report file names",
        ["SelectionParts"] = "named constants: the part key the select step records its unexpected errors under",
        ["KeepParts"] = "named constants: the part key the keep step records its unexpected errors under",
        ["FitParts"] = "named constants: the part key the fit step records its unexpected errors under",
        ["TooCloseParts"] = "named constants: the part key the too-close step records its unexpected errors under",
        ["ChangedTerrainParts"] = "named constants: the part key the changed-terrain step records its unexpected errors under",
        ["RestingObjectsParts"] = "named constants: the part keys the also-remove step records its unexpected errors under",
        ["LeftBehindParts"] = "named constants: the part key the left-behind step records its unexpected errors under",
        ["VisibleLeftBehindParts"] = "named constants: the part key the visible left-behind step records its unexpected errors under",
        ["MarkerMoveParts"] = "named constants: the part key the marker-move step records its unexpected errors under",
        ["ReportFileParts"] = "named constants: the part key the report step records its unexpected errors under",
        ["ReferenceScale"] = "one-line rule over a contract value: a reference's scale",
        ["TargetVisibility"] = "one-line rule over contract types: how a target looks in the game",
        ["LeftBehindOutcomes"] = "one-line rule over a contract enum: whether an outcome is a removal",
        ["VisibleLeftBehindOutcomes"] = "one-line rule over a contract enum: whether an outcome is a removal",
        ["SupporterShares"] = "one-line rule over contract records: the total share of a support category",
        ["ContainingObjects"] = "read-only lookup of the object containing each target",
        ["Replacements"] = "read-only lookup of the replaced other-mod objects",
        ["ObjectQueryScratch"] = "per-worker buffers for the object queries",
    };

    /// <summary>
    /// Methods and computed properties (a getter with a body) on the records and structs of the contracts, each
    /// "Type.Member" with why it is allowed. Auto-properties and init-only properties are not listed.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedMembers = new()
    {
        ["CollectedObjects.Other"] = "index accessor",
        ["CollectedObjects.TargetSpaces"] = "accessor over the collected objects",
        ["CollectedObjects.OtherModAndSupportOnlyObjects"] = "accessor joining two collected lists",
        ["CollectedObjects.Item"] = "indexer",
        ["NpcBodySet.Source"] = "one-line value derived from its own bodies",
        ["NpcBodySet.FirstPoint"] = "one-line value derived from its own bodies",
        ["LeftBehindCheck.IsRemoved"] = "one-line rule over its own outcome",
        ["VisibleLeftBehindCheck.IsRemoved"] = "one-line rule over its own outcome",
        ["NpcBody.FromBox"] = "factory",
        ["NpcBody.Point"] = "factory",
        ["NpcSizeCounts.Add"] = "one-line sum of two counts",
        ["NpcWork.Zero"] = "factory for the empty value",
        ["ObjectVisibility.Invisible"] = "factory",
        ["ObjectVisibility.Visible"] = "factory for a fixed value",
        ["ObjectVisibility.MissingBase"] = "factory for a fixed value",
        ["ObjectVisibility.EffectOnly"] = "factory for a fixed value",
        ["ObjectVisibility.IsVisible"] = "one-line value derived from its own fields",
        ["OtherObject.Transform"] = "one-line value derived from its own fields",
        ["OtherObject.IsPlacedNpc"] = "one-line value derived from its own fields",
        ["OtherModsObjectOutcome.NewPosition"] = "one-line value derived from its own fields",
        ["OtherModsObjectOutcome.IsTakenAway"] = "one-line value derived from its own fields",
        ["OtherModsObjectsResult.TakenAway"] = "the objects as the step leaves them, read by the later steps and derived only from its own outcomes",
        ["TargetObject.Placement"] = "one-line value derived from its own fields",
        ["TargetObject.PlacedAt"] = "one-line copy of itself at a given placement, for the rules asked about a shrunk or turned object",
        ["RunSettings.DetailedLog"] = "one-line value derived from its own fields: the one setting that has the report files, so it is stored once",
        ["ModTrustTable.IsPatch"] = "one-line rule over its own field",
        ["ModTrustTable.IsTrustedItself"] = "one-line fact over its own entries: a rule names the plugin itself as trusted",
        ["ModTrustTable.IsBaseGame"] = "one-line fact over its own entries: the plugin is one of the game's own",
        ["ModTrustTable.IsMasterOfCleanedMod"] = "one-line fact over its own entries: the cleaned mod lists the plugin as a master",
        ["ModTrustTable.IsNeverTrusted"] = "one-line fact over its own entries: the user listed the plugin as never trusted",
        ["ModTrustTable.VersionTrustOf"] = "the one mapping of a plugin's trust to the version trust of the accepted-value rule, over its own fields",
        ["ChangedTerrainSettings.CountsWaterChanges"] = "one-line value derived from its own fields: either water switch, asked by the validator, the step and the log",
        ["ShapeZoneWork.Zero"] = "factory for the empty value",
        ["SupportEvaluation.RemovedShare"] = "one-line sum over its own shares",
        ["SupportEvaluation.MainRemovedSupporter"] = "one-line pick from its own shares",
        ["Supporter.Placed"] = "factory",
        ["Supporter.Target"] = "factory",
        ["TooCloseWork.Zero"] = "factory for the empty value",
    };

    /// <summary>The name of the generic Lazy type; every delegate type (Func, Action, custom ones) is found by its base type instead.</summary>
    private static readonly Regex CallbackTypePattern = new(@"^Lazy`\d+$");

    /// <summary>
    /// Contract members that hold a Lazy or a delegate (Func, Action or a custom one), also inside generic arguments, arrays or nullables, each "Type.Member" with why it is allowed. Any other one is a
    /// finding to report, not to list.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedCallbackMembers = new()
    {
        ["RestingObjectsInput.TimeSinceStart"] = "steps may not read the clock themselves; the entry hands them the clock",
        ["RunEnvironment.TimeSinceStart"] = "steps may not read the clock themselves; the entry hands them the clock",
        ["ObjectMeasurementRules.OtherModObjects"] = "a step's rule handed to a cache at creation (Q3)",
        ["ObjectMeasurementRules.ObjectsOfAnyPlugin"] = "a step's rule handed to a cache at creation (Q3)",
        ["NpcBodyRules.CreateOwnBodyResolver"] = "a step's rule handed to a cache at creation (Q3)",
        ["NpcBodyRules.DistinctBodies"] = "a step's rule handed to a cache at creation (Q3)",
        ["PlacedNpcRules.IsPlacedNpc"] = "a step's rule handed to a cache at creation (Q3)",
        ["PlacedNpcRules.BodyTransform"] = "a step's rule handed to a cache at creation (Q3)",
        ["PlacedNpcRules.CountSizes"] = "a step's rule handed to a cache at creation (Q3)",
    };

    [Fact]
    public void ContractsHoldNoLazyFuncOrActionMemberExceptTheListedOnes()
    {
        var members = ContractsTypes()
            .SelectMany(type => type.GetProperties(AllDeclared).Cast<MemberInfo>().Concat(type.GetFields(AllDeclared))
                .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && IsCallbackType(MemberType(member)))
                .Select(member => $"{BusinessCode.PathName(type)}.{member.Name}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        var notListed = members.Except(AllowedCallbackMembers.Keys).ToList();
        var notFound = AllowedCallbackMembers.Keys.Except(members).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            notListed.Count == 0 && notFound.Count == 0,
            $"Contract members that hold a Lazy, Func or Action (not listed): {string.Join(", ", notListed)}. Listed but not found: {string.Join(", ", notFound)}");
    }

    private static Type MemberType(MemberInfo member) =>
        member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

    /// <summary>A Lazy or a delegate of any kind, also inside generic arguments, arrays and nullables.</summary>
    private static bool IsCallbackType(Type type)
    {
        if (type.HasElementType) return IsCallbackType(type.GetElementType()!);
        if (typeof(Delegate).IsAssignableFrom(type)) return true;
        if (!type.IsGenericType) return false;
        return CallbackTypePattern.IsMatch(type.GetGenericTypeDefinition().Name) || type.GetGenericArguments().Any(IsCallbackType);
    }

    private delegate int CustomCallback(int value);

    [Fact]
    public void TheCallbackScanSeesLazyAndDelegatesInsideGenericArgumentsArraysAndNullables()
    {
        Assert.True(IsCallbackType(typeof(Lazy<int>)));
        Assert.True(IsCallbackType(typeof(Func<int>)));
        Assert.True(IsCallbackType(typeof(Action)));
        Assert.True(IsCallbackType(typeof(CustomCallback)));
        Assert.True(IsCallbackType(typeof(System.Collections.Immutable.ImmutableArray<Func<int>>)));
        Assert.True(IsCallbackType(typeof(IReadOnlyList<Lazy<int>>)));
        Assert.True(IsCallbackType(typeof(Dictionary<string, CustomCallback>)));
        Assert.True(IsCallbackType(typeof(Func<int>[])));
        Assert.True(IsCallbackType(typeof((int, Action)?)));
        Assert.False(IsCallbackType(typeof(int)));
        Assert.False(IsCallbackType(typeof(IReadOnlyList<string>)));
        Assert.False(IsCallbackType(typeof(Dictionary<string, int[]>)));
    }

    [Fact]
    public void NoTwoProductionTypesShareAName()
    {
        var shared = WrittenTopLevelTypes(ProductionSources.Assemblies())
            .GroupBy(type => type.Name)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(type => type.FullName).Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void NoProductionTypeIsNamedLikeTheLastPartOfItsNamespace()
    {
        var clashes = WrittenTopLevelTypes(ProductionSources.Assemblies())
            .Where(type => type.Namespace is not null && type.Name == type.Namespace[(type.Namespace.LastIndexOf('.') + 1)..])
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(clashes);
    }

    [Fact]
    public void EveryContractsTypeIsPublic()
    {
        var hidden = ContractsTypes().Where(type => !type.IsVisible).Select(BusinessCode.PathName).Order(StringComparer.Ordinal).ToList();

        Assert.Empty(hidden);
    }

    [Fact]
    public void ContractsHoldOnlyRecordsInterfacesEnumsAndReadonlyStructs()
    {
        var others = ContractsTypes().Where(type => !IsDataShape(type)).Select(BusinessCode.PathName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(AllowedOtherTypes.Keys.Order(StringComparer.Ordinal), others);
    }

    [Fact]
    public void ContractRecordsAndStructsCarryNoMethodsOrComputedPropertiesExceptTheListedOnes()
    {
        var members = ContractsTypes()
            .Where(type => IsDataShape(type) && !type.IsInterface && !type.IsEnum)
            .SelectMany(type => type.GetMethods(AllDeclared)
                .Where(IsWrittenMethodOrComputedGetter)
                .Select(method => $"{BusinessCode.PathName(type)}.{MemberName(method)}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        var notListed = members.Except(AllowedMembers.Keys).ToList();
        var notFound = AllowedMembers.Keys.Except(members).Order(StringComparer.Ordinal).ToList();

        Assert.True(notListed.Count == 0 && notFound.Count == 0, $"Not listed: {string.Join(", ", notListed)}. Listed but not found: {string.Join(", ", notFound)}");
    }

    private static bool IsWrittenMethodOrComputedGetter(MethodInfo method) =>
        (!method.IsSpecialName || method.Name.StartsWith(GetterPrefix, StringComparison.Ordinal))
            && !RecordSynthesisedMethods.Contains(method.Name)
            && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static string MemberName(MethodInfo method) =>
        method.Name.StartsWith(GetterPrefix, StringComparison.Ordinal) ? method.Name[GetterPrefix.Length..] : method.Name;

    private static IEnumerable<Type> WrittenTopLevelTypes(IEnumerable<Assembly> assemblies) =>
        assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.DeclaringType is null && IsWritten(type));

    /// <summary>The written types of the contracts projects, nested ones included.</summary>
    private static IEnumerable<Type> ContractsTypes() =>
        ContractsAssemblies().SelectMany(assembly => assembly.GetTypes()).Where(IsWritten);

    private static bool IsWritten(Type type) => !BusinessCode.IsCompilerGenerated(type) && !type.Name.StartsWith('<');

    private static IReadOnlyList<Assembly> ContractsAssemblies() =>
        ProductionSources.Assemblies().Where(IsContractsProject).ToList();

    private static bool IsContractsProject(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        return name.EndsWith(ContractsSuffix, StringComparison.Ordinal) && Rules.Projects.Any(rule => rule.Name == name);
    }

    private static bool IsDataShape(Type type) =>
        type.IsInterface
            || type.IsEnum
            || type.GetMethod("<Clone>$", AllDeclared) is not null
            || (type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
}
