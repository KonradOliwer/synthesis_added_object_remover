using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Rules over the built types of all production projects: type names are unique, and the contracts projects (the rows of kind C
/// and Q in the project table) hold only public data shapes. A contracts type carries no method, operator, computed property, accessor
/// with a body, constructor or initialiser unless it is listed with a reason, and every listed one is a single expression of at most
/// <see cref="MaxMemberLines"/> lines unless it is listed as over that budget with a reason.
/// </summary>
public class ProductionTypeTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private const string GetterPrefix = "get_";
    private const string SetterPrefix = "set_";
    private const string ConstructorName = ".ctor";
    private const string ConstructorKeySuffix = "." + ConstructorName;
    private const string IndexerReflectionName = "Item";
    private const string IndexerSourceName = "this";
    private const string OperatorPrefix = "op_";
    private const int MaxMemberLines = 10;

    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The source name of each operator a contracts type writes, by the name the compiled method has.</summary>
    private static readonly Dictionary<string, string> OperatorSourceNames = new() { ["op_Addition"] = "operator +" };

    /// <summary>
    /// Listed members that are longer than <see cref="MaxMemberLines"/> lines, each "Type.Member" with why it is allowed: a rule that
    /// several steps need and that cannot be shorter.
    /// </summary>
    private static readonly Dictionary<string, string> OverBudgetMembers = new()
    {
        ["TargetObject.RestoredAs"] = "the one rule of how a restore changes an object, applied by the decision list, the judge and the restore step; it names every part a restore can put back, so its length follows the parts",
    };

    /// <summary>
    /// Static classes that hold only named values, each with what it names. They carry no method; a value they hold (a constant, or a
    /// static field or property set where it is declared) needs no listing in <see cref="AllowedMembers"/>, but is a single expression.
    /// </summary>
    private static readonly Dictionary<string, string> NamedConstantTypes = new()
    {
        ["MeshLimits"] = "named constants: the mesh size limits",
        ["MarkerMoveLimits"] = "named constants: the marker move limit",
        ["OtherModsObjectsLimits"] = "named constants: how far another mod's object is pulled down",
        ["OtherModsObjectsParts"] = "named constants: the part keys the step for other mods' objects records its unexpected errors under",
        ["RemovalConvention"] = "named constants: the game's convention for a placed object switched off for good",
        ["ReportFileNames"] = "named constants: the report file names",
        ["SettingNames"] = "named constants: the names the settings screen shows, which the settings screen and the validation warnings share",
        ["SelectionParts"] = "named constants: the part key the select step records its unexpected errors under",
        ["KeepParts"] = "named constants: the part key the keep step records its unexpected errors under",
        ["FitParts"] = "named constants: the part key the fit step records its unexpected errors under",
        ["JudgeChangedObjectsParts"] = "named constants: the part key the judge-changed-objects step records its unexpected errors under",
        ["TooCloseParts"] = "named constants: the part key the too-close step records its unexpected errors under",
        ["ChangedTerrainParts"] = "named constants: the part key the changed-terrain step records its unexpected errors under",
        ["RestingObjectsParts"] = "named constants: the part keys the also-remove step records its unexpected errors under",
        ["AlsoRemoveRounds"] = "named constants: how the also-remove rounds open, whichever step decides them",
        ["LeftBehindParts"] = "named constants: the part key the left-behind step records its unexpected errors under",
        ["VisibleLeftBehindParts"] = "named constants: the part key the visible left-behind step records its unexpected errors under",
        ["MarkerMoveParts"] = "named constants: the part key the marker-move step records its unexpected errors under",
        ["ReportFileParts"] = "named constants: the part key the report step records its unexpected errors under",
        ["RestoreWhereRemovedParts"] = "named constants: the part keys the restore-where-removed step records its unexpected errors under",
    };

    /// <summary>
    /// Contract types that are not a record, interface, enum, readonly struct or set of named constants, each with why it is allowed.
    /// Every method, computed property, constructor and initialiser of one is listed in <see cref="AllowedMembers"/> on its own.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedRuleAndLookupTypes = new()
    {
        ["ReferenceScale"] = "rule over a contract value: a reference's scale",
        ["TargetVisibility"] = "rule over contract types: how a target looks in the game",
        ["LeftBehindOutcomes"] = "one-line rules over a contract enum: whether an outcome is a removal or a restoration",
        ["SupporterShares"] = "rules over contract records: the total share of a support category, and whether a category left its place",
        ["TooCloseZoneReach"] = "how far the zones of the too-close rule that are on reach from the centre of a box, restated from the too-close step's zone growth and held equal to it by a test",
        ["ContainingObjects"] = "read-only lookup of the object containing each target",
        ["Replacements"] = "read-only lookup of the replaced other-mod objects",
        ["ObjectQueryScratch"] = "per-worker buffers for the object queries",
    };

    /// <summary>
    /// Members of the contracts that carry logic, each "Type.Member" with why it is allowed: a method, a computed property, an
    /// accessor with a body ("Type.Property"), a constructor ("Type..ctor"), and a field or property with an initialiser. Auto-properties,
    /// init-only properties without a body, constants and the values of <see cref="NamedConstantTypes"/> are not listed. Every listed
    /// member is a single expression; a longer one belongs in the step that uses it.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedMembers = new()
    {
        ["CollectedObjects.Other"] = "index accessor",
        ["CollectedObjects.TargetSpaces"] = "accessor over the collected objects",
        ["CollectedObjects.OtherModAndSupportOnlyObjects"] = "accessor joining two collected lists",
        ["CollectedObjects.Item"] = "indexer",
        ["CollectedObjects.WinningPresence"] = "the empty default of a lookup the collector fills",
        ["CollectedObjects.WinningPrimitives"] = "the empty default of a lookup the collector fills",
        ["NpcBodySet.Source"] = "single-expression value derived from its own bodies",
        ["NpcBodySet.FirstPoint"] = "single-expression value derived from its own bodies",
        ["LeftBehindVerdict.IsRemoved"] = "single-expression rule over its own outcome",
        ["LeftBehindVerdict.IsRestored"] = "single-expression rule over its own outcome",
        ["FitSettings.ShrinksOrTurns"] = "single-expression rule over its own limits",
        ["FitSettings.Off"] = "the fixed settings of the switched-off step",
        ["BaseShape.Missing"] = "the fixed shape of a base object that could not be resolved",
        ["NpcBody.FromBox"] = "factory",
        ["NpcBody.Point"] = "factory",
        ["NpcSizeCounts.Add"] = "single-expression sum of two counts",
        ["NpcWork.Zero"] = "factory for the empty value",
        ["ObjectVisibility.Invisible"] = "factory",
        ["ObjectVisibility.Visible"] = "factory for a fixed value",
        ["ObjectVisibility.MissingBase"] = "factory for a fixed value",
        ["ObjectVisibility.EffectOnly"] = "factory for a fixed value",
        ["ObjectVisibility.IsVisible"] = "single-expression value derived from its own fields",
        ["ObjectVisibility.IsUsedByActorsOrMap"] = "single-expression value derived from its own fields",
        ["RestingObjectsResult.EvaluatedSupport"] = "single-expression value derived from its own fields",
        ["RestingObjectsResult.DetailsHoldTouch"] = "single-expression value derived from its own fields",
        ["RestingObjectsResult.DetailsHoldMeshOrigins"] = "single-expression value derived from its own fields",
        ["OtherObject.Transform"] = "single-expression value derived from its own fields",
        ["OtherObject.IsPlacedNpc"] = "single-expression value derived from its own fields",
        ["OtherModsObjectOutcome.NewPosition"] = "single-expression value derived from its own fields",
        ["OtherModsObjectOutcome.IsTakenAway"] = "single-expression value derived from its own fields",
        ["OtherModsObjectOutcome.IsRemoved"] = "single-expression value derived from its own fields",
        ["OtherModsObjectsResult.TakenAway"] = "the objects as the step leaves them, read by the later steps and derived only from its own outcomes",
        ["OtherModsObjectsResult.SwitchedOff"] = "the fixed result of the switched-off step",
        ["HeldUpTargets.SwitchedOff"] = "the fixed result of the switched-off step",
        ["ReportTables.SwitchedOff"] = "the fixed result of the switched-off step",
        ["MarkerMoves.SwitchedOff"] = "the fixed result of the switched-off step",
        ["MarkerMoves.NotPerformedAfterLeftBehindStopped"] = "the fixed result of the step when the left-behind step before it stopped",
        ["LeftBehindResult.SwitchedOff"] = "the fixed result of the switched-off step",
        ["RestoreWhereRemovedResult.SwitchedOff"] = "the fixed result of the switched-off step",
        ["ReportFileDetails.None"] = "the fixed value for no details",
        ["ReportFileDetails.StoppedByEarlierFailure"] = "the fixed value for a step an earlier failure stopped",
        ["ReportFileDetails.SwitchedOff"] = "the fixed value for the switched-off step",
        ["WeightedSurfaceSamples.Empty"] = "the fixed value for no samples",
        ["RoundEffect.Untouched"] = "the fixed effect of a round that did nothing to an object",
        ["TargetObject.Placement"] = "single-expression value derived from its own fields",
        ["TargetObject.PlacedAt"] = "single-expression copy of itself at a given placement, for the rules asked about a shrunk or turned object",
        ["TargetObject.RestoredAs"] = "copy of itself with the parts that go back taken from its earlier version, for the views of the objects as they stand and the old-place check",
        ["NavmeshChangedByCleanedMod.Winner"] = "single-expression choice between two of its own fields, the version the game shows",
        ["NavmeshChangedByCleanedMod.ChangesTriangles"] = "single-expression test of two of its own fields, shared by the navmesh plan and the restore step's switched-off case",
        ["TargetObject.AsOtherObject"] ="field-by-field copy of itself as another plugin's object, for the worlds the old-place check and the restore guards judge in",
        ["EarlierVersion.GoesBackToEarlierList"] = "single-expression rule over its own fields, shared by the collector, which judges the cell a restore writes into, and the patch writing",
        ["EarlierVersion.GoesBackToEarlierCell"] = "single-expression rule over its own fields and the record's cell now, shared by the collector, which judges the cell a restore writes into, and the patch writing",
        ["RunSettings.DetailedLog"] = "single-expression value derived from its own fields: the one setting that has the report files, so it is stored once",
        ["ModTrustTable.IsPatch"] = "single-expression rule over its own field",
        ["ModTrustTable.IsTrustedItself"] = "single-expression fact over its own entries: a rule names the plugin itself as trusted",
        ["ModTrustTable.IsBaseGame"] = "single-expression fact over its own entries: the plugin is one of the game's own",
        ["ModTrustTable.IsMasterOfCleanedMod"] = "single-expression fact over its own entries: the cleaned mod lists the plugin as a master",
        ["ModTrustTable.IsNeverTrusted"] = "single-expression fact over its own entries: the user listed the plugin as never trusted",
        ["ModTrustTable.VersionTrustOf"] = "the one mapping of a plugin's trust to the version trust of the accepted-value rule",
        ["ModTrustTable.AcceptedWorld"] = "the world the cleaned mod's objects are judged in, built once from the table's own plugins by the one mapping of plugin trust to version trust",
        ["ModTrustTable.AcceptedWorldWithoutCleanedMod"] = "the world without the cleaned mod, built once from the table's own plugins",
        ["ChangedTerrainSettings.CountsWaterChanges"] = "single-expression value derived from its own fields: either water switch is on",
        ["ChangedTerrainSettings.CountsAChange"] = "single-expression value derived from its own fields: any of the three kinds of change is counted",
        ["ChangedTerrainSettings.HasAnArea"] = "single-expression value derived from its own fields: either terrain area is on",
        ["ChangedTerrainSettings.Runs"] = "single-expression rule over its own fields: on, and counting a change in at least one area, so the step could find something",
        ["ChangedTerrainSettings.AsRun"] = "single-expression copy with the switch as the step runs, so the step and the log compare groups as they run",
        ["WhereRemovedRestoreSettings.Runs"] = "single-expression rule over its own switches: the step runs when any kind goes back",
        ["ScanNeeds.UnionOf"] = "the one union of the steps' needs",
        ["ScanNeeds.Union"] = "the flag-by-flag union of two steps' needs",
        ["ScanNeeds.UnionOfAreaRecordVersions"] = "the area record reads come from the one step that asks for them",
        ["MarkerMoveSettings.Needs"] = "what the step needs the scan to read, derived from its own settings",
        ["ReportFileSettings.Needs"] = "what the log and the report files need the scan to read, derived from their own settings",
        ["ChangedTerrainSettings.Needs"] = "what the step needs the scan to read, derived from its own settings",
        ["ChangedTerrainSettings.NeedsOfBoth"] = "what the step needs the scan to read for its two groups of objects, and whether only the changed objects need it",
        ["ChangedObjectSettings.NeedsWhenReplacedObjectsAre"] = "what the step needs the scan to read, derived from its own settings",
        ["WhereRemovedRestoreSettings.Needs"] = "what the step needs the scan to read, derived from its own settings",
        ["WhereRemovedRestoreSettings.ReadAreaRecords"] = "the area records the step's guards look at, derived from its own switches",
        ["ShapeZoneWork.Zero"] = "factory for the empty value",
        ["ShapeZoneWork.op_Addition"] = "the sum of two work counts, field by field, which the tool that adds up the counts of the workers needs",
        ["NpcWork.op_Addition"] = "the sum of two work counts, field by field, which the tool that adds up the counts of the workers needs",
        ["TooCloseWork.op_Addition"] = "the sum of two work counts, part by part, which the tool that adds up the counts of the workers needs",
        ["SupportEvaluation.RemovedShare"] = "single-expression sum over its own shares",
        ["SupportEvaluation.RestoredShare"] = "single-expression sum over its own shares",
        ["SupportEvaluation.LeftItsPlaceShare"] = "single-expression sum over its own shares",
        ["SupportEvaluation.MainLeftItsPlaceSupporter"] = "single-expression pick from its own shares",
        ["Supporter.Placed"] = "factory",
        ["Supporter.Target"] = "factory",
        ["Supporter.Terrain"] = "the fixed supporter that stands for the terrain",
        ["TooCloseWork.Zero"] = "factory for the empty value",
        ["ChangedTerrainPass.SwitchedOff"] = "factory for a pass of a switched-off step",
        ["JudgedPasses.Outcome"] = "single-expression rule over its own passes: the step is switched off when every pass is",
        ["JudgedPasses.Proposals"] = "the one merge of the passes' proposals into target order",
        ["JudgedPasses.Failures"] = "the one merge of the passes' failures into target order",
        ["CollisionGroup.TooClose"] = "single-expression view of its objects with the too-close part of its settings",
        ["CollisionGroup.ChangedTerrain"] = "single-expression view of its objects with the changed-terrain part of its settings",
        ["CollisionGroup.Fit"] = "single-expression view of its objects with the shrink-or-turn part of its settings",
        ["RunSettings.CollisionGroupsOf"] = "one group of the target objects of each origin with the collision settings of that origin, handed to the steps as their groups",
        ["RunSettings.CollisionOf"] = "the pick of the collision settings of an origin",
        ["ChangedObjectCollision.Section"] = "single-expression view of the section's settings as typed",
        ["ChangedObjectCollision.ForMoved"] = "single-expression view of the section's settings for moved objects: the limits only when their switch is on",
        ["ChangedObjectCollision.ForReplaced"] = "single-expression view of the section's settings for replaced objects: the limits only when their switch is on",
        ["SelectedObjects.ObjectsThatCanCauseRemovals"] = "single-expression pick of the view for the standard NPC handling",
        ["SelectedObjects.ViewFor"] = "single-expression lookup of the view for an NPC handling",
        ["SectorAreas.Total"] = "index accessor",
        ["SectorAreas.Removed"] = "index accessor",
        ["SectorAreas.Restored"] = "index accessor",
        ["SectorAreas.State"] = "index accessor",
        ["SectorAreas.OccupiedCount"] = "single-expression count over its own states",
        ["SectorAreas.ClearedCount"] = "single-expression count over its own states",
        ["SectorAreas.RemovedCount"] = "single-expression count over its own states",
        ["SectorAreas.RestoredCount"] = "single-expression count over its own states",
        ["Decision.Effect"] = "the one reading of what the decision's round did to the object, one arm per kind of decision",
        ["ObjectsJudgedWith.MergeEqualSettings"] = "single-expression factory over the tool's merge: the groups with equal settings as one group each",
        ["Decision.EffectIn"] = "single-expression rule over its own round",
        ["Decision.FollowedARestoredMember"] = "single-expression rule over its own reason and the decision of the group member it names",
        ["LandQuarterJudged.Verdict"] = "single-expression value derived from its own area",
        ["TerrainPieces.Any"] = "single-expression value derived from its own fields",
        ["ReferenceScale.Normalize"] = "the one rule of a placed reference's scale",
        ["TargetVisibility.VisibilityOf"] = "the one question of how a target looks, asked of the base object shapes",
        ["LeftBehindOutcomes.IsRemoval"] = "single-expression rule over the outcome enum",
        ["LeftBehindOutcomes.IsRestoration"] = "single-expression rule over the outcome enum",
        ["IPlacementRule.CheckWhereItStands"] = "single-expression default member: the protocol's first check, the check of the object where it stands",
        ["ChangedTerrainSettings.AreasAround"] = "single-expression rule over its own fields: the areas around an object whose ground is checked",
        ["SupporterShares.TotalOf"] = "single-expression sum over a category of the shares",
        ["SupporterShares.HasLeftItsPlace"] = "single-expression rule over the support category enum",
        ["TooCloseZoneReach.CornerReach"] = "the corner factor of a box, a fact of geometry",
        ["TooCloseZoneReach.ReachRadius"] = "the reach of the zones that are on, from the centre of a box of the given half extents",
        ["Replacements..ctor"] = "private constructor of the factories: it builds the set of replaced objects",
        ["Replacements.Count"] = "single-expression count of the list",
        ["Replacements.None"] = "factory",
        ["Replacements.Of"] = "factory",
        ["Replacements.Excluding"] = "factory",
        ["Replacements.IsReplaced"] = "single-expression lookup in the set of replaced objects",
        ["ContainingObjects.Of"] = "single-expression lookup by target index",
        ["ObjectQueryScratch.Spatial"] = "the buffer each worker owns",
        ["ObjectQueryScratch.Triangles"] = "the buffer each worker owns",
        ["ObjectQueryScratch.Matches"] = "the buffer each worker owns",
        ["ObjectQueryScratch.Others"] = "the buffer each worker owns",
        ["Supporter..ctor"] = "private constructor of the factories: it only stores the type and the index",
        ["RunOutcome..ctor"] = "constructor that stores what the run decided and found",
    };

    /// <summary>The name of the generic Lazy type; every delegate type (Func, Action, custom ones) is found by its base type instead.</summary>
    private static readonly Regex CallbackTypePattern = new(@"^Lazy`\d+$");

    /// <summary>The members of the contracts projects as their source files write them, by the path of their type.</summary>
    private static readonly Lazy<ILookup<string, WrittenMember>> WrittenMembers = new(ReadWrittenMembers);

    /// <summary>
    /// Contract members that hold a Lazy or a delegate (Func, Action or a custom one), also inside generic arguments, arrays or nullables, each "Type.Member" with why it is allowed. Any other one is a
    /// finding to report, not to list.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedCallbackMembers = new()
    {
        ["RunEnvironment.TimeSinceStart"] = "steps may not read the clock themselves; the entry hands them the clock",
        ["ObjectMeasurementRules.OtherModObjects"] = "a step's rule handed to a cache at creation",
        ["ObjectMeasurementRules.ObjectsOfAnyPlugin"] = "a step's rule handed to a cache at creation",
        ["NpcBodyRules.CreateOwnBodyResolver"] = "a step's rule handed to a cache at creation",
        ["NpcBodyRules.DistinctBodies"] = "a step's rule handed to a cache at creation",
        ["PlacedNpcRules.IsPlacedNpc"] = "a step's rule handed to a cache at creation",
        ["PlacedNpcRules.BodyTransform"] = "a step's rule handed to a cache at creation",
        ["PlacedNpcRules.CountSizes"] = "a step's rule handed to a cache at creation",
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
    public void ContractsHoldOnlyRecordsInterfacesEnumsReadonlyStructsAndTheListedOtherTypes()
    {
        var others = ContractsTypes().Where(type => !IsDataShape(type)).Select(BusinessCode.PathName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(NamedConstantTypes.Keys.Concat(AllowedRuleAndLookupTypes.Keys).Order(StringComparer.Ordinal), others);
    }

    [Fact]
    public void ContractTypesCarryNoMethodsAccessorsConstructorsOrInitialisersExceptTheListedOnes()
    {
        var found = FindLogicMembers();

        var notListed = found.Where(member => !AllowedMembers.ContainsKey(member.Key)).Select(member => $"{member.Key} ({member.Value})").ToList();
        var notFound = AllowedMembers.Keys.Except(found.Keys).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            notListed.Count == 0 && notFound.Count == 0,
            $"Contract members with logic that are not listed in {nameof(AllowedMembers)} (list with a reason, or move into the step that uses them):{Environment.NewLine}"
                + $"{string.Join(Environment.NewLine, notListed)}{Environment.NewLine}Listed but not found (remove):{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}");
    }

    [Fact]
    public void EveryListedContractMemberAndEveryNamedConstantIsOneExpression()
    {
        var notFound = new List<string>();
        var longer = new List<string>();
        foreach (var key in AllowedMembers.Keys.Order(StringComparer.Ordinal))
        {
            var members = SourceMembersOfKey(key).ToList();
            if (members.Count == 0) notFound.Add(key);
            longer.AddRange(members.Where(member => !member.IsOneExpression).Select(member => $"{key} ({member.Where})"));
        }
        longer.AddRange(
            WrittenMembers.Value.SelectMany(group => group)
                .Where(member => NamedConstantTypes.ContainsKey(member.Type) && member.HasInitialiser && !member.IsOneExpression)
                .Select(member => $"{member.Place} ({member.Where})"));

        Assert.True(
            longer.Count == 0 && notFound.Count == 0,
            $"Members of the contracts longer than one expression (listed in {nameof(AllowedMembers)} or a named constant; move into the step that uses them):{Environment.NewLine}"
                + $"{string.Join(Environment.NewLine, longer)}{Environment.NewLine}Listed but not found in the source:{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}");
    }

    [Fact]
    public void EveryListedContractMemberIsWithinTheLineBudgetExceptTheListedOnes()
    {
        var overBudget = AllowedMembers.Keys
            .Where(key => SourceMembersOfKey(key).Any(member => member.LineCount > MaxMemberLines))
            .Order(StringComparer.Ordinal)
            .ToList();

        var notListed = overBudget.Except(OverBudgetMembers.Keys).ToList();
        var notNeeded = OverBudgetMembers.Keys.Except(overBudget).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            notListed.Count == 0 && notNeeded.Count == 0,
            $"Listed members longer than {MaxMemberLines} lines (shorten them, move them into the step that uses them, or list them in {nameof(OverBudgetMembers)} with a reason): {string.Join(", ", notListed)}. "
                + $"Listed in {nameof(OverBudgetMembers)} but within the budget (remove): {string.Join(", ", notNeeded)}");
    }

    [Fact]
    public void TheMemberReaderCountsTheLinesOfAMemberAndNamesAnOperatorBySymbol()
    {
        const string source = """
            class Sample
            {
                public static Sample operator +(Sample a, Sample b) => a;
                public int Block(int x)
                {
                    var y = x;
                    return y;
                }
            }
            """;

        var members = WrittenMember.Of("Sample.cs", source).Select(member => $"{member.Name} lines={member.LineCount} oneExpression={member.IsOneExpression}");

        Assert.Equal(["operator + lines=1 oneExpression=True", "Block lines=5 oneExpression=False"], members);
    }

    [Fact]
    public void TheMemberReaderSeesConstructorsInitialisersAndExpressionBodies()
    {
        const string source = """
            class Sample
            {
                private const int Limit = 3;
                private static readonly int[] Cache = [1];
                public int Value { get; } = 2;
                public Sample(int a)
                {
                    Value = a;
                }
                public int Twice(int x) => x * 2;
                public int Block(int x)
                {
                    return x;
                }
                public int Statements(int x) => Run(y => { Use(y); return y; });
                public int Prop { get => 1; }
                public int Computed => a == b ? 1 : 2;
            }
            """;

        var members = WrittenMember.Of("Sample.cs", source)
            .Select(member => $"{member.Name} constructor={member.IsConstructor} initialiser={member.HasInitialiser} oneExpression={member.IsOneExpression}");

        Assert.Equal(
            [
                "Limit constructor=False initialiser=False oneExpression=False",
                "Cache constructor=False initialiser=True oneExpression=True",
                "Value constructor=False initialiser=True oneExpression=True",
                "Sample constructor=True initialiser=False oneExpression=False",
                "Twice constructor=False initialiser=False oneExpression=True",
                "Block constructor=False initialiser=False oneExpression=False",
                "Statements constructor=False initialiser=False oneExpression=False",
                "Prop constructor=False initialiser=False oneExpression=False",
                "Computed constructor=False initialiser=False oneExpression=True",
            ],
            members);
    }

    /// <summary>"Type.Member" of each member that carries logic, with where it is written.</summary>
    private static SortedDictionary<string, string> FindLogicMembers()
    {
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in ContractsTypes().Where(type => !type.IsEnum))
        {
            var path = BusinessCode.PathName(type);
            foreach (var name in WrittenMethodAndAccessorNames(type)) found.TryAdd($"{path}.{name}", WhereIsMember(path, name));
            foreach (var member in WrittenMembers.Value[path].Where(NeedsListing)) found.TryAdd($"{path}.{(member.IsConstructor ? ConstructorName : member.Name)}", member.Where);
        }
        return found;
    }

    /// <summary>A constructor, or a field or property with an initialiser; the values of a set of named constants need no listing.</summary>
    private static bool NeedsListing(WrittenMember member) =>
        member.IsConstructor || (member.HasInitialiser && !NamedConstantTypes.ContainsKey(member.Type));

    private static IEnumerable<string> WrittenMethodAndAccessorNames(Type type) =>
        type.GetMethods(AllDeclared).Where(method => IsWrittenMethodOrAccessor(type, method)).Select(MemberName);

    /// <summary>An interface member counts only when it has a body (a default implementation); the members the compiler writes for records do not.</summary>
    private static bool IsWrittenMethodOrAccessor(Type type, MethodInfo method) =>
        (!type.IsInterface || !method.IsAbstract)
            && (!method.IsSpecialName || IsAccessorOfProperty(method) || IsOperator(method))
            && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsAccessorOfProperty(MethodInfo method) =>
        method.Name.StartsWith(GetterPrefix, StringComparison.Ordinal) || method.Name.StartsWith(SetterPrefix, StringComparison.Ordinal);

    private static bool IsOperator(MethodInfo method) => method.Name.StartsWith(OperatorPrefix, StringComparison.Ordinal);

    private static string MemberName(MethodInfo method) =>
        IsAccessorOfProperty(method) ? method.Name[GetterPrefix.Length..] : method.Name;

    private static string WhereIsMember(string typePath, string reflectedName) =>
        WrittenMembers.Value[typePath].Where(member => member.Name == SourceNameOf(reflectedName)).Select(member => member.Where).FirstOrDefault() ?? "source not found";

    private static string SourceNameOf(string reflectedName) =>
        reflectedName == IndexerReflectionName ? IndexerSourceName : OperatorSourceNames.GetValueOrDefault(reflectedName, reflectedName);

    private static IEnumerable<WrittenMember> SourceMembersOfKey(string key)
    {
        if (key.EndsWith(ConstructorKeySuffix, StringComparison.Ordinal))
        {
            return WrittenMembers.Value[key[..^ConstructorKeySuffix.Length]].Where(member => member.IsConstructor);
        }
        var split = key.LastIndexOf('.');
        var name = SourceNameOf(key[(split + 1)..]);
        return WrittenMembers.Value[key[..split]].Where(member => member.Name == name);
    }

    private static ILookup<string, WrittenMember> ReadWrittenMembers()
    {
        var contractsProjects = Rules.Projects.Where(Rules.IsContracts).Select(rule => rule.Name).ToHashSet();
        return WrittenMember.Of(SourceFile.All().Where(file => contractsProjects.Contains(file.Project))).ToLookup(member => member.Type);
    }

    private static IEnumerable<Type> WrittenTopLevelTypes(IEnumerable<Assembly> assemblies) =>
        assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.DeclaringType is null && IsWritten(type));

    /// <summary>The written types of the contracts projects, nested ones included.</summary>
    private static IEnumerable<Type> ContractsTypes() =>
        ContractsAssemblies().SelectMany(assembly => assembly.GetTypes()).Where(IsWritten);

    private static bool IsWritten(Type type) => !BusinessCode.IsCompilerGenerated(type) && !type.Name.StartsWith('<');

    private static IReadOnlyList<Assembly> ContractsAssemblies() =>
        ProductionSources.Assemblies().Where(IsContractsProject).ToList();

    /// <summary>A project whose row in the project table has a contracts kind (C or Q).</summary>
    private static bool IsContractsProject(Assembly assembly) =>
        Rules.Projects.Any(rule => rule.Name == assembly.GetName().Name && Rules.IsContracts(rule));

    private static bool IsDataShape(Type type) =>
        type.IsInterface
            || type.IsEnum
            || type.GetMethod("<Clone>$", AllDeclared) is not null
            || (type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
}
