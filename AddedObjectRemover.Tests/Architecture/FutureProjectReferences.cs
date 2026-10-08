using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>One use of a type of one future project by a file that will sit in another.</summary>
internal sealed record ImpliedReference(string From, string To, string File, string Use);

/// <param name="UnplacedFiles">Source files that map to no row of the table and are not in the planned-home lists.</param>
/// <param name="AmbiguousTypes">Type names declared in more than one row; uses of them cannot be attributed.</param>
/// <param name="RowsWithCode">Rows that have at least one source file today.</param>
internal sealed record ReferenceScan(
    IReadOnlyList<string> UnplacedFiles,
    IReadOnlyList<string> AmbiguousTypes,
    IReadOnlySet<string> RowsWithCode,
    IReadOnlyList<ImpliedReference> References);

/// <summary>
/// Finds which future project each source file will belong to and which other future projects it uses, so the
/// project table can be checked before the projects exist. A file's row comes from its folder (the folders already
/// match the table's), or from <see cref="PlannedHomes"/> for code still at the root of the entry project.
/// A use is the name of a top-level type declared in another row, or a fully qualified Steps/Caches/Run name.
/// Names stand in for namespaces because the tools all share the root namespace, and a using of a namespace
/// adds nothing the type names do not show.
/// Not seen: a call of an extension method whose class is not named, and nested types.
/// </summary>
internal static partial class FutureProjectReferences
{
    private const string EntryRow = "AddedObjectRemover";
    private const string NamespaceRoot = "AddedObjectRemover.";

    /// <summary>Code that still sits where its row's folder does not say (the root of the entry project, or a mixed place) and the row it goes to (design section 9).</summary>
    private static readonly Dictionary<string, string[]> PlannedHomes = new()
    {
        [EntryRow] = ["Program.cs", "Settings.cs", "LogSink.cs", "ConsoleLogFile.cs", "Steps/BuildLogAndReports/CsvReportOutput.cs"],
        ["UnexpectedFailureHandling"] = ["Failures.cs", "UnexpectedError.cs"],
        ["PluginRecordReadingAndWriting"] =
        [
            "PluginRecords.cs", "PluginRecordsFactory.cs", "RecordKeyConversion.cs", "RecordHandles.cs", "LoadOrderPluginsReader.cs", "BaseFactsReader.cs",
            "PlacedRecordReader.cs", "PlacedRecordLocation.cs", "PlacedRecordExtensions.cs", "PlacedOverrideWriter.cs", "PlacedRecordWriter.cs",
            "LinkReader.cs", "LandscapeTerrain.cs", "LandHeightReader.cs", "NavmeshRecords.cs", "NavmeshTriangles.cs", "CellNavmesh.cs",
            "NpcRecordReads.cs", "NpcTraitsReader.cs", "NpcTemplateChain.cs",
        ],
        ["MeshFileReading"] =
        [
            "MeshFiles.cs", "MeshFileSource.cs", "MeshFilesFactory.cs", "AssetProblemLog.cs", "MalformedNifException.cs", "NiflyCalls.cs", "NifFooterWorkaround",
            "NifGeometry.cs", "NifGeometryReader.cs", "NifReadResult.cs", "NifRootFinder.cs", "NifShapes.cs", "NifShapeCollector.cs", "NodeTransformResolver.cs",
            "RenderGeometryTypes.cs", "AvObjectFlags.cs", "Similarity.cs",
        ],
    };

    /// <summary>Types declared in a file that is mixed: their row is not the row of the file.</summary>
    private static readonly Dictionary<string, string> MixedFileTypeHomes = new()
    {
        ["ILogSink"] = "RunAllSteps.Contracts",
    };

    /// <summary>Words that can stand before a type name where it is used, not declared.</summary>
    private static readonly HashSet<string> WordsBeforeATypeUse =
        ["is", "as", "new", "return", "case", "or", "and", "not", "in", "when", "typeof", "nameof", "await", "throw", "yield", "else", "using", "static", "out", "ref", "default"];

    [GeneratedRegex(@"^[ \t]*(?:global\s+)?using\s[^\r\n]*;[ \t]*\r?$|^[ \t]*namespace\s[^\r\n]*\r?$", RegexOptions.Multiline)]
    private static partial Regex UsingAndNamespaceLinePattern();

    [GeneratedRegex(@"\bAddedObjectRemover\.(?:Steps|Caches|Run)\.[\w.]+")]
    private static partial Regex QualifiedNamePattern();

    /// <summary>A name not reached through a dot; a name followed by an opening parenthesis is a method call unless it is a constructor call.</summary>
    [GeneratedRegex(@"(?<![\w.@])(?<new>new\s+)?(?<name>[A-Za-z_]\w*)(?<call>(?:<[^<>()]*>)?\s*\()?")]
    private static partial Regex NamePattern();

    /// <summary>A member, parameter or variable declaration: a word, then the declared name, then what ends a declaration.</summary>
    [GeneratedRegex(@"(?<![.\w])(?<type>[\w>\]?]+)\s+(?<name>[A-Za-z_]\w*)(?:<[^<>()]*>)?\s*(?=[({=;,)]|=>)")]
    private static partial Regex DeclarationPattern();

    public static ReferenceScan Scan(ProjectRules rules)
    {
        var placed = new List<(SourceFile File, string Row)>();
        var unplaced = new List<string>();
        foreach (var file in SourceFile.All())
        {
            var row = RowOf(file, rules);
            if (row is null) unplaced.Add(file.Description);
            else placed.Add((file, row));
        }

        var typeRows = TypeRows(placed, out var ambiguous);
        var references = placed
            .SelectMany(item => ReferencesOf(item.File, item.Row, rules, typeRows))
            .Distinct()
            .OrderBy(reference => reference.From, StringComparer.Ordinal).ThenBy(reference => reference.To, StringComparer.Ordinal)
            .ThenBy(reference => reference.File, StringComparer.Ordinal).ThenBy(reference => reference.Use, StringComparer.Ordinal)
            .ToList();
        return new ReferenceScan(unplaced.Order(StringComparer.Ordinal).ToList(), ambiguous, placed.Select(item => item.Row).ToHashSet(), references);
    }

    public static string? RowOf(SourceFile file, ProjectRules rules)
    {
        var planned = PlannedHomes.Where(home => file.IsIn(home.Value)).Select(home => home.Key).SingleOrDefault();
        return planned ?? rules.Projects
            .Where(rule => rule.Kind != ProjectRules.EntryKind && rule.Kind != ProjectRules.TestKind && file.IsIn([rule.Folder]))
            .OrderByDescending(rule => rule.Folder.Length)
            .Select(rule => rule.Name)
            .FirstOrDefault();
    }

    /// <summary>The row of a Steps, Caches or Run namespace (the longest folder that is a prefix of it), or null for any other namespace.</summary>
    public static string? RowOfNamespace(string ns, ProjectRules rules)
    {
        if (!ns.StartsWith(NamespaceRoot, StringComparison.Ordinal)) return null;
        var rest = ns[NamespaceRoot.Length..];
        return rules.Projects
            .Select(rule => (rule.Name, Namespace: rule.Folder.Replace('/', '.')))
            .Where(candidate => rest == candidate.Namespace || rest.StartsWith(candidate.Namespace + ".", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Namespace.Length)
            .Select(candidate => candidate.Name)
            .FirstOrDefault();
    }

    /// <summary>A qualified type name belongs to the row its type was placed in (a type may have moved on while its namespace stayed); anything else to its namespace's row.</summary>
    private static string? RowOfQualifiedName(string name, ProjectRules rules, IReadOnlyDictionary<string, string> typeRows) =>
        typeRows.GetValueOrDefault(name[(name.LastIndexOf('.') + 1)..]) ?? RowOfNamespace(name, rules);

    private static Dictionary<string, string> TypeRows(IReadOnlyList<(SourceFile File, string Row)> placed, out List<string> ambiguous)
    {
        var rowsByType = placed
            .SelectMany(item => SourceFile.DeclaredTypes(item.File.Code).Select(type => (Type: type, Row: MixedFileTypeHomes.GetValueOrDefault(type, item.Row))))
            .GroupBy(item => item.Type)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Row).Distinct().Order(StringComparer.Ordinal).ToList());
        ambiguous = rowsByType.Where(item => item.Value.Count > 1)
            .Select(item => $"{item.Key}: {string.Join(", ", item.Value)}")
            .Order(StringComparer.Ordinal).ToList();
        return rowsByType.Where(item => item.Value.Count == 1).ToDictionary(item => item.Key, item => item.Value[0]);
    }

    private static IEnumerable<ImpliedReference> ReferencesOf(SourceFile file, string row, ProjectRules rules, IReadOnlyDictionary<string, string> typeRows)
    {
        var code = UsingAndNamespaceLinePattern().Replace(file.CodeWithInterpolatedExpressions, "");
        var qualified = QualifiedNamePattern().Matches(code)
            .Select(match => (Row: RowOfQualifiedName(match.Value, rules, typeRows), Use: match.Value));
        var named = UsedTypeNames(code)
            .Where(typeRows.ContainsKey)
            .Select(type => (Row: (string?)typeRows[type], Use: type));
        return qualified.Concat(named)
            .Where(use => use.Row is not null && use.Row != row)
            .Select(use => new ImpliedReference(row, use.Row!, file.Path, use.Use));
    }

    /// <summary>Names that are used as a type, not declared as a member, parameter or variable in the same code, and not a method call.</summary>
    private static IEnumerable<string> UsedTypeNames(string code)
    {
        var declared = DeclaredNames(code);
        return NamePattern().Matches(code)
            .Where(match => !match.Groups["call"].Success || match.Groups["new"].Success)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !declared.Contains(name))
            .Distinct();
    }

    /// <summary>A name declared with its own name as the type (a property named like its type) is not counted: it is also a real use.</summary>
    private static HashSet<string> DeclaredNames(string code) =>
        DeclarationPattern().Matches(code)
            .Where(match => !WordsBeforeATypeUse.Contains(match.Groups["type"].Value) && match.Groups["type"].Value != match.Groups["name"].Value)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet();
}
