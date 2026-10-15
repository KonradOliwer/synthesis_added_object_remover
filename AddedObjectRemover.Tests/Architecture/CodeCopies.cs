namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One stretch of code that appears elsewhere too. Members are the "Type.Member" names its lines are in, "Type" for
/// lines of a type outside its members, and "" for lines outside every type.
/// </summary>
internal sealed record CopiedRun(string File, int FirstLine, int LastLine, IReadOnlyList<string> Members)
{
    /// <summary>Each place as "path:Type.Member", or just the path for code outside every type.</summary>
    public IEnumerable<string> Places => Members.Select(member => member == "" ? File : $"{File}:{member}");

    public override string ToString() =>
        $"    {File}:{FirstLine}-{LastLine}  {string.Join(", ", Members.Select(member => member == "" ? "(outside every type)" : member))}";
}

/// <summary>
/// Runs that copy each other, or sit in the same places. The key lists every place the family occurs in, so it
/// changes only when a copy appears in or disappears from a member, never when other code next to a copy changes.
/// </summary>
internal sealed record CopyFamily(IReadOnlyList<CopiedRun> Runs)
{
    private const string PlaceSeparator = " | ";

    public string Key => KeyOf(Runs.SelectMany(run => run.Places));

    public static string KeyOf(IEnumerable<string> places) => string.Join(PlaceSeparator, places.Distinct().Order(StringComparer.Ordinal));

    public override string ToString() => $"{Key}{Environment.NewLine}{string.Join(Environment.NewLine, Runs)}";
}

/// <summary>
/// Finds code copied between or inside files: every run of consecutive token windows whose content also occurs
/// somewhere else without overlapping. Runs that share any window belong to one family, and families that occur in
/// the same places are one. Short copies below the window length are not found. A run that spans two members is
/// placed in both; code of a type outside every member is placed in the type; code outside every type is placed in
/// the file.
/// </summary>
internal static class CodeCopies
{
    private const ulong HashBase = 1_099_511_628_211UL;
    private const ulong HashSeed = 14_695_981_039_346_656_037UL;

    private readonly record struct WindowRun(int File, int FirstWindow, int LastWindow);

    public static IReadOnlyList<CopyFamily> Find(IReadOnlyList<(string Name, string Source)> files, int windowLength)
    {
        var tokenLines = files.Select(file => CodeTokens.Significant(file.Source)).ToList();
        var windowHashes = tokenLines.Select(tokens => WindowHashes(tokens, windowLength)).ToList();
        var occurrences = OccurrencesByHash(windowHashes);
        var outlines = files.Select(file => CodeOutline.Of(file.Source)).ToList();

        var runs = FindRuns(windowHashes, occurrences, windowLength);
        return GroupIntoFamilies(runs, windowHashes, occurrences, windowLength)
            .Select(family => new CopyFamily(family.Select(run => Describe(run, files[run.File].Name, tokenLines[run.File], outlines[run.File], windowLength)).ToList()))
            .GroupBy(family => family.Key)
            .Select(sameKey => new CopyFamily(sameKey.SelectMany(family => family.Runs).ToList()))
            .ToList();
    }

    private static List<WindowRun> FindRuns(List<ulong[]> windowHashes, Dictionary<ulong, List<(int File, int Start)>> occurrences, int windowLength)
    {
        var runs = new List<WindowRun>();
        for (var file = 0; file < windowHashes.Count; file++)
        {
            var hashes = windowHashes[file];
            var runStart = -1;
            for (var start = 0; start <= hashes.Length; start++)
            {
                var copied = start < hashes.Length && IsCopied(occurrences[hashes[start]], file, start, windowLength);
                if (copied && runStart < 0) runStart = start;
                if (!copied && runStart >= 0)
                {
                    runs.Add(new WindowRun(file, runStart, start - 1));
                    runStart = -1;
                }
            }
        }
        return runs;
    }

    private static List<List<WindowRun>> GroupIntoFamilies(
        List<WindowRun> runs, List<ulong[]> windowHashes, Dictionary<ulong, List<(int File, int Start)>> occurrences, int windowLength)
    {
        var runOfWindow = new Dictionary<(int File, int Start), int>();
        for (var run = 0; run < runs.Count; run++)
        {
            for (var window = runs[run].FirstWindow; window <= runs[run].LastWindow; window++) runOfWindow[(runs[run].File, window)] = run;
        }

        var parent = Enumerable.Range(0, runs.Count).ToArray();
        for (var run = 0; run < runs.Count; run++)
        {
            for (var window = runs[run].FirstWindow; window <= runs[run].LastWindow; window++)
            {
                foreach (var other in occurrences[windowHashes[runs[run].File][window]])
                {
                    if (IsSeparateCopy(other, runs[run].File, window, windowLength)) parent[RootOf(parent, run)] = RootOf(parent, runOfWindow[other]);
                }
            }
        }
        return runs.Index().GroupBy(entry => RootOf(parent, entry.Index)).Select(group => group.Select(entry => entry.Item).ToList()).ToList();
    }

    private static int RootOf(int[] parent, int run)
    {
        while (parent[run] != run) run = parent[run];
        return run;
    }

    private static CopiedRun Describe(WindowRun run, string file, IReadOnlyList<CodeToken> tokens, CodeOutline outline, int windowLength)
    {
        var lastToken = run.LastWindow + windowLength - 1;
        var lines = tokens.Skip(run.FirstWindow).Take(lastToken - run.FirstWindow + 1).Select(token => token.Line).Distinct();
        var members = lines.SelectMany(line => MembersOfLine(outline, line)).Distinct().Order(StringComparer.Ordinal).ToList();
        return new CopiedRun(file, tokens[run.FirstWindow].Line, tokens[lastToken].Line, members);
    }

    private static IReadOnlyList<string> MembersOfLine(CodeOutline outline, int line)
    {
        var places = outline.PlacesOfLine(line);
        return places.Count == 0 ? [""] : places;
    }

    private static bool IsCopied(List<(int File, int Start)> sameContent, int file, int start, int windowLength) =>
        sameContent.Any(other => IsSeparateCopy(other, file, start, windowLength));

    private static bool IsSeparateCopy((int File, int Start) other, int file, int start, int windowLength) =>
        other.File != file || Math.Abs(other.Start - start) >= windowLength;

    private static Dictionary<ulong, List<(int File, int Start)>> OccurrencesByHash(List<ulong[]> windowHashes)
    {
        var occurrences = new Dictionary<ulong, List<(int File, int Start)>>();
        for (var file = 0; file < windowHashes.Count; file++)
        {
            for (var start = 0; start < windowHashes[file].Length; start++)
            {
                var hash = windowHashes[file][start];
                if (!occurrences.TryGetValue(hash, out var list)) occurrences[hash] = list = [];
                list.Add((file, start));
            }
        }
        return occurrences;
    }

    private static ulong[] WindowHashes(IReadOnlyList<CodeToken> tokens, int windowLength)
    {
        if (tokens.Count < windowLength) return [];
        var tokenHashes = tokens.Select(token => TextHash(token.Text)).ToArray();
        var highestPower = 1UL;
        for (var step = 1; step < windowLength; step++) highestPower *= HashBase;

        var hashes = new ulong[tokens.Count - windowLength + 1];
        ulong rolling = 0;
        for (var index = 0; index < tokenHashes.Length; index++)
        {
            if (index >= windowLength) rolling -= tokenHashes[index - windowLength] * highestPower;
            rolling = rolling * HashBase + tokenHashes[index];
            if (index >= windowLength - 1) hashes[index - windowLength + 1] = rolling;
        }
        return hashes;
    }

    private static ulong TextHash(string text)
    {
        var hash = HashSeed;
        foreach (var character in text) hash = (hash ^ character) * HashBase;
        return hash;
    }
}
