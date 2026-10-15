using System.Reflection;
using System.Text.RegularExpressions;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The runner only wires: it hands each step its own settings record and decides nothing about the domain. Three rules,
/// checked on the source text (comments and literals removed): it reads no member of a settings record, it has no
/// conditions of its own, and it holds no error boundary. A new settings read, condition or error boundary fails here.
/// </summary>
public class RunnerRulesTests
{
    private const string RunnerFolder = "Run/RunAllSteps";
    private const string RunCachesFolder = "Run/RunAllSteps/RunCaches";

    /// <summary>"file: Record.Member" of a member read on a settings record, and why the runner may read it. Expected empty.</summary>
    private static readonly Dictionary<string, string> AllowedSettingsReads = new();

    /// <summary>"file: condition line (whitespace collapsed)" in the runner, and why the runner decides it.</summary>
    private static readonly Dictionary<string, string> AllowedConditions = new()
    {
        ["StepRunner.cs: return request switch"] =
            "The settings validation may have ended the run before any step; the runner passes its stop on.",
        ["StepRunner.cs: if (collected.Stop is { } stop)"] =
            "Step 2 returns the stop for a run without target objects; the runner ends the run on any stop a step returns.",
    };

    [Fact]
    public void TheRunnerReadsNoMemberOfAnySettingsRecord()
    {
        var settings = SettingsOfRunSettings();

        var found = RunnerAndRunCachesFiles()
            .SelectMany(file => FindSettingsReads(file.Code, settings).Select(read => $"{FileName(file)}: {read}"))
            .ToHashSet();

        AssertOnlyAllowed(found, AllowedSettingsReads.Keys, "Member read on the settings in the runner; the step reads its own settings", nameof(AllowedSettingsReads));
    }

    [Fact]
    public void TheRunnerHasNoConditionsOfItsOwnBesidesTheAllowedStops()
    {
        var found = RunnerFiles()
            .SelectMany(file => FindConditionLines(file.Code).Select(line => $"{FileName(file)}: {line}"))
            .ToHashSet();

        AssertOnlyAllowed(found, AllowedConditions.Keys, "Condition in the runner; the domain decision belongs to a step", nameof(AllowedConditions));
    }

    [Fact]
    public void TheRunnerHoldsNoErrorBoundary()
    {
        var found = RunnerFiles()
            .SelectMany(file => FindErrorBoundaryUses(file.Code).Select(use => $"{FileName(file)}: {use}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            found.Count == 0,
            $"The runner holds an error boundary; each part catches its own failures:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void TheErrorBoundaryReaderSeesPartKeysPartFailuresGuardFailuresTryAndCatch()
    {
        const string code = """
            var a = report.Guard(new PartKey("Run", "Files"), () => 1, () => 2);
            var b = Failures.Attempt(() => 1, _ => 2);
            try { Run(); } catch (Exception) { }
            errors.Add(new PartFailure(part, failure, null));
            """;

        Assert.Equal(["Failures.", "Guard(", "PartFailure", "PartKey", "catch", "try"], FindErrorBoundaryUses(code).Order(StringComparer.Ordinal));
        Assert.Empty(FindErrorBoundaryUses("var c = ReportOutput.Write(settings, tables);"));
    }

    [Fact]
    public void TheSettingsRecordsComeFromTheRunSettingsRecord()
    {
        var settings = SettingsOfRunSettings();

        Assert.Equal(
            ["AlsoRemoveSettings", "ChangedTerrainSettings", "Execution", "LeftBehindSettings", "MarkerMoveSettings", "ModIdentificationSettings", "ReportFileSettings", "SelectionSettings", "TooCloseSettings", "WhatToCollect"],
            settings.RecordTypes.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["AlsoRemove", "ChangedTerrain", "Execution", "LeftBehind", "MarkerMoves", "ModIdentification", "Read", "Reports", "Selection", "TooClose"],
            settings.RecordProperties.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["DetailedLog", "ExcludedNames", "Target", "TargetAsTyped"],
            settings.PlainProperties.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSettingsReaderSeesDirectAccessTypedVariablesVarLocalsAndPlainProperties()
    {
        var settings = SettingsOfRunSettings();
        const string code = """
            var a = settings.TooClose.Npcs;
            var b = settings.Selection?.Npcs;
            var local = settings.LeftBehind;
            use(local.Radius);
            void Use(MarkerMoveSettings moves) => use(moves.Distance);
            use(settings.Execution);
            use(settings.DetailedLog);
            use(ready.Settings.TargetAsTyped);
            use(world.Targets);
            """;

        var reads = FindSettingsReads(code, settings);

        Assert.Equal(
            ["LeftBehind.Radius", "MarkerMoveSettings moves.Distance", "RunSettings.DetailedLog", "RunSettings.TargetAsTyped", "Selection.Npcs", "TooClose.Npcs"],
            reads.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheConditionReaderSeesTheConditionFormsButNotNullableTypes()
    {
        const string code = """
            if (a) return;
            var b = c switch { 1 => 2, _ => 3 };
            var d = e is null;
            var f = g
                ? h
                : i;
            var j = k ? l : m;
            int? n = o ?? p;
            q ??= r;
            int? s = t;
            catch (X x) when (x.Y) { }
            var u = v.Where(w => w);
            var y = z.Any();
            var aa = bb && cc;
            var dd = ee || ff;
            var gg = hh.Select(ii => ii);
            var jj = kk.All(ll => ll);
            var mm = nn.First();
            var oo = pp.FirstOrDefault();
            var qq = rr.Count();
            var ss = tt.TakeWhile(uu => uu);
            var vv = ww?.Length;
            var xx = yy.Count;
            long? zz = 1;
            """;

        var lines = FindConditionLines(code);

        Assert.Equal(
            new[]
            {
                "? h", "catch (X x) when (x.Y) { }", "if (a) return;", "int? n = o ?? p;", "q ??= r;", "var aa = bb && cc;", "var b = c switch { 1 => 2, _ => 3 };",
                "var d = e is null;", "var dd = ee || ff;", "var j = k ? l : m;", "var jj = kk.All(ll => ll);", "var mm = nn.First();",
                "var oo = pp.FirstOrDefault();", "var qq = rr.Count();", "var ss = tt.TakeWhile(uu => uu);", "var u = v.Where(w => w);",
                "var vv = ww?.Length;", "var y = z.Any();",
            },
            lines.Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> FindErrorBoundaryUses(string code) =>
        [.. Regex.Matches(code, @"\bPartKey\b|\bPartFailure\b|\bGuard\(|\bFailures\.|\btry\b|\bcatch\b").Select(match => match.Value).Distinct()];

    /// <summary>The run caches take every rule from the steps, so they are scanned for settings reads too.</summary>
    private static IEnumerable<SourceFile> RunnerAndRunCachesFiles() => SourceFile.All().Where(file => file.IsIn([RunnerFolder]));

    private static IEnumerable<SourceFile> RunnerFiles() =>
        SourceFile.All().Where(file => file.IsIn([RunnerFolder]) && !file.Path.StartsWith(RunCachesFolder + "/", StringComparison.Ordinal));

    private static string FileName(SourceFile file) => System.IO.Path.GetFileName(file.Path);

    /// <param name="RecordTypes">The types of the settings records.</param>
    /// <param name="RecordProperties">The RunSettings properties that hold a settings record.</param>
    /// <param name="PlainProperties">The other RunSettings properties.</param>
    private sealed record RunSettingsMembers(List<string> RecordTypes, List<string> RecordProperties, List<string> PlainProperties);

    private static RunSettingsMembers SettingsOfRunSettings()
    {
        var properties = typeof(RunSettings).GetProperties()
            .Select(property => (Property: property.Name, Type: Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .ToList();
        var recordProperties = properties.Where(item => IsRecord(item.Type)).ToList();
        return new(
            [.. recordProperties.Select(item => item.Type.Name).Distinct()],
            [.. recordProperties.Select(item => item.Property)],
            [.. properties.Where(item => !IsRecord(item.Type)).Select(item => item.Property)]);
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    /// <summary>
    /// "Record.Member" for a member read on a settings record: through a RunSettings property (<c>settings.TooClose.Npcs</c>),
    /// through a name declared with a record type, or through a <c>var</c> local assigned from such a property.
    /// A read of one of RunSettings' other properties (<c>settings.DetailedLog</c>) is "RunSettings.Property".
    /// </summary>
    private static IReadOnlyList<string> FindSettingsReads(string code, RunSettingsMembers settings)
    {
        var propertyNames = string.Join("|", settings.RecordProperties.Select(Regex.Escape));
        var typeNames = string.Join("|", settings.RecordTypes.Select(Regex.Escape));
        var plainNames = string.Join("|", settings.PlainProperties.Select(Regex.Escape));

        var reads = Regex.Matches(code, $@"\.(?<record>{propertyNames})\s*\??\.\s*(?<member>\w+)")
            .Select(match => $"{match.Groups["record"].Value}.{match.Groups["member"].Value}")
            .Concat(Regex.Matches(code, $@"\.(?<plain>{plainNames})\b").Select(match => $"RunSettings.{match.Groups["plain"].Value}"))
            .ToList();

        var typedNames = Regex.Matches(code, $@"\b(?<type>{typeNames})\??\s+(?<name>\w+)\b")
            .Select(match => (Name: match.Groups["name"].Value, Label: $"{match.Groups["type"].Value} {match.Groups["name"].Value}"));
        var localNames = Regex.Matches(code, $@"\bvar\s+(?<name>\w+)\s*=\s*[\w.?]*\.(?<record>{propertyNames})\s*;")
            .Select(match => (Name: match.Groups["name"].Value, Label: match.Groups["record"].Value));

        foreach (var (name, label) in typedNames.Concat(localNames))
        {
            reads.AddRange(Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(name)}\s*\??\.\s*(?<member>\w+)")
                .Select(match => $"{label}.{match.Groups["member"].Value}"));
        }

        return reads;
    }

    /// <summary>
    /// The lines (whitespace collapsed) that hold an <c>if</c>, <c>switch</c>, <c>is</c>, <c>when</c>, a conditional operator,
    /// <c>Where(</c>, <c>Any(</c>, <c>All(</c>, <c>First(</c>, <c>FirstOrDefault(</c>, <c>Count(</c> (the call, not the property),
    /// <c>TakeWhile(</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>??</c> or <c>?.</c>.
    /// </summary>
    private static IReadOnlyList<string> FindConditionLines(string code)
    {
        var starts = Regex.Matches(code, @"\b(?:if|switch|when|is)\b|\b(?:Where|Any|All|First|FirstOrDefault|Count|TakeWhile)\s*\(|&&|\|\||\?\?|(?<!\?)\?\.").Select(match => match.Index)
            .Concat(Regex.Matches(code, @"\s\?\s").Select(match => match.Index + match.Value.IndexOf('?')));
        return [.. starts.Select(index => LineAt(code, index)).Distinct()];
    }

    private static string LineAt(string code, int index)
    {
        var start = code.LastIndexOf('\n', index) + 1;
        var end = code.IndexOf('\n', index);
        var line = end < 0 ? code[start..] : code[start..end];
        return Regex.Replace(line.Trim(), @"\s+", " ");
    }

    private static void AssertOnlyAllowed(IReadOnlySet<string> found, IEnumerable<string> allowed, string problem, string listName)
    {
        var allowedSet = allowed.ToHashSet();
        var notAllowed = found.Except(allowedSet).Order(StringComparer.Ordinal).ToList();
        var stale = allowedSet.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"{problem}:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        Assert.True(stale.Count == 0, $"Allowed in {listName} but no longer in the runner (remove):{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }
}
