namespace AddedObjectRemover.Tests.Architecture;

/// <summary>One stretch of code that appears elsewhere too, in the member "Type.Member".</summary>
internal sealed record CopiedRun(string File, int FirstLine, int LastLine, int TokenCount, string Member)
{
    /// <summary>The place as "path:Type.Member".</summary>
    public string Place => $"{File}:{Member}";

    public override string ToString() => $"    {File}:{FirstLine}-{LastLine} ({TokenCount} tokens)  {Member}";
}

/// <summary>
/// Runs that copy each other, or sit in the same places. The key lists every place the family occurs in, so it
/// changes only when a copy appears in or disappears from a member, never when other code next to a copy changes.
/// </summary>
internal sealed record CopyFamily(IReadOnlyList<CopiedRun> Runs)
{
    private const string PlaceSeparator = " | ";

    public string Key => KeyOf(Runs.Select(run => run.Place));

    public static string KeyOf(IEnumerable<string> places) => string.Join(PlaceSeparator, places.Distinct().Order(StringComparer.Ordinal));

    public override string ToString() => $"{Key}{Environment.NewLine}{string.Join(Environment.NewLine, Runs)}";
}

/// <summary>
/// Finds code copied between or inside files: every run of consecutive token windows whose content also occurs
/// somewhere else without overlapping. Runs that share any window belong to one family, and families that occur in
/// the same places are one. Short copies below the window length are not found.
/// <para>
/// What counts as a copy of logic:
/// <list type="bullet">
/// <item>A window lies inside one member. A window across two members, or in a type outside its members, only meets
/// the shape of declarations: one member's end and the next one's start, lists of fields, properties, constants or
/// enum members, a record's parameters.</item>
/// <item>A window is not a list: a pattern of at most a third of the window that repeats through most of it (items,
/// switch arms or per-axis terms on one line). <see cref="CodeTokens"/> drops lists written one item per line.</item>
/// <item>The names of two windows map one to one: a copy may rename, but renames every use of a name alike. Two
/// stretches whose names map otherwise (rows against columns, x against y) do different things in the same shape.</item>
/// </list>
/// </para>
/// </summary>
internal static class CodeCopies
{
    private const ulong HashBase = 1_099_511_628_211UL;
    private const ulong HashSeed = 14_695_981_039_346_656_037UL;

    /// <summary>A list repeats its pattern at least this often in a window.</summary>
    private const int MinRepeatsOfAList = 3;

    /// <summary>In a list, at least this share of a window's tokens equal the token one pattern later.</summary>
    private const double SameTokenShareOfAList = 0.8;

    private readonly record struct WindowRun(int File, int FirstWindow, int LastWindow);

    private readonly record struct Window(int File, int Start);

    public static IReadOnlyList<CopyFamily> Find(IReadOnlyList<(string Name, string Source)> files, int windowLength)
    {
        var tokens = files.Select(file => CodeTokens.Significant(file.Source)).ToList();
        var outlines = files.Select(file => CodeOutline.Of(file.Source, file.Name)).ToList();
        var tokenHashes = tokens.Select(fileTokens => fileTokens.Select(token => TextHash(token.Text)).ToArray()).ToList();
        var windowHashes = tokenHashes.Select(hashes => WindowHashes(hashes, windowLength)).ToList();
        var comparable = tokens.Select((fileTokens, file) => ComparableWindows(fileTokens, tokenHashes[file], outlines[file], windowLength)).ToList();
        var occurrences = OccurrencesByHash(windowHashes, comparable);

        bool IsCopy(Window window, Window other) =>
            IsSeparate(window, other, windowLength) && NamesMapOneToOne(tokens[window.File], window.Start, tokens[other.File], other.Start, windowLength);

        var runs = FindRuns(windowHashes, comparable, occurrences, IsCopy);
        return GroupIntoFamilies(runs, windowHashes, occurrences, IsCopy)
            .Select(family => new CopyFamily(family.Select(run => Describe(run, files[run.File].Name, tokens[run.File], outlines[run.File], windowLength)).ToList()))
            .GroupBy(family => family.Key)
            .Select(sameKey => new CopyFamily(sameKey.SelectMany(family => family.Runs).ToList()))
            .ToList();
    }

    private static List<WindowRun> FindRuns(
        List<ulong[]> windowHashes, List<bool[]> comparable, Dictionary<ulong, List<Window>> occurrences, Func<Window, Window, bool> isCopy)
    {
        var runs = new List<WindowRun>();
        for (var file = 0; file < windowHashes.Count; file++)
        {
            var runStart = -1;
            for (var start = 0; start <= windowHashes[file].Length; start++)
            {
                var window = new Window(file, start);
                var copied = start < windowHashes[file].Length && comparable[file][start] && occurrences[windowHashes[file][start]].Any(other => isCopy(window, other));
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
        List<WindowRun> runs, List<ulong[]> windowHashes, Dictionary<ulong, List<Window>> occurrences, Func<Window, Window, bool> isCopy)
    {
        var runOfWindow = new Dictionary<Window, int>();
        for (var run = 0; run < runs.Count; run++)
        {
            for (var start = runs[run].FirstWindow; start <= runs[run].LastWindow; start++) runOfWindow[new Window(runs[run].File, start)] = run;
        }

        var parent = Enumerable.Range(0, runs.Count).ToArray();
        for (var run = 0; run < runs.Count; run++)
        {
            for (var start = runs[run].FirstWindow; start <= runs[run].LastWindow; start++)
            {
                var window = new Window(runs[run].File, start);
                foreach (var other in occurrences[windowHashes[window.File][start]])
                {
                    if (isCopy(window, other)) parent[RootOf(parent, run)] = RootOf(parent, runOfWindow[other]);
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
        var member = outline.OnlyMemberOfLine(tokens[run.FirstWindow].Line)!.Place;
        return new CopiedRun(file, tokens[run.FirstWindow].Line, tokens[lastToken].Line, lastToken - run.FirstWindow + 1, member);
    }

    private static bool IsSeparate(Window window, Window other, int windowLength) =>
        other.File != window.File || Math.Abs(other.Start - window.Start) >= windowLength;

    private static bool NamesMapOneToOne(IReadOnlyList<CodeToken> first, int firstStart, IReadOnlyList<CodeToken> second, int secondStart, int windowLength)
    {
        var forward = new Dictionary<string, string>(StringComparer.Ordinal);
        var backward = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var offset = 0; offset < windowLength; offset++)
        {
            if (first[firstStart + offset].Text != CodeTokens.Name) continue;
            var name = first[firstStart + offset].Word;
            var other = second[secondStart + offset].Word;
            if (forward.TryGetValue(name, out var mapped) && mapped != other) return false;
            if (backward.TryGetValue(other, out var mappedBack) && mappedBack != name) return false;
            forward[name] = other;
            backward[other] = name;
        }
        return true;
    }

    private static Dictionary<ulong, List<Window>> OccurrencesByHash(List<ulong[]> windowHashes, List<bool[]> comparable)
    {
        var occurrences = new Dictionary<ulong, List<Window>>();
        for (var file = 0; file < windowHashes.Count; file++)
        {
            for (var start = 0; start < windowHashes[file].Length; start++)
            {
                if (!comparable[file][start]) continue;
                var hash = windowHashes[file][start];
                if (!occurrences.TryGetValue(hash, out var list)) occurrences[hash] = list = [];
                list.Add(new Window(file, start));
            }
        }
        return occurrences;
    }

    private static bool[] ComparableWindows(IReadOnlyList<CodeToken> tokens, ulong[] tokenHashes, CodeOutline outline, int windowLength)
    {
        var windowCount = Math.Max(0, tokens.Count - windowLength + 1);
        var memberRunStarts = MemberRunStarts(tokens, outline);
        var lists = ListWindows(tokenHashes, windowLength, windowCount);
        return Enumerable.Range(0, windowCount).Select(start => memberRunStarts[start + windowLength - 1] <= start && !lists[start]).ToArray();
    }

    /// <summary>For each token, where the stretch of tokens in the same one member up to it starts; past the token for a token in no single member.</summary>
    private static int[] MemberRunStarts(IReadOnlyList<CodeToken> tokens, CodeOutline outline)
    {
        var memberOfLine = tokens.Select(token => token.Line).Distinct().ToDictionary(line => line, outline.OnlyMemberOfLine);
        var runStarts = new int[tokens.Count];
        for (var index = 0; index < tokens.Count; index++)
        {
            var member = memberOfLine[tokens[index].Line];
            var continues = index > 0 && member is not null && member == memberOfLine[tokens[index - 1].Line];
            runStarts[index] = member is null ? index + 1 : continues ? runStarts[index - 1] : index;
        }
        return runStarts;
    }

    private static bool[] ListWindows(ulong[] tokenHashes, int windowLength, int windowCount)
    {
        var lists = new bool[windowCount];
        for (var pattern = 1; pattern <= windowLength / MinRepeatsOfAList; pattern++)
        {
            var pairsInWindow = windowLength - pattern;
            var neededPairs = Math.Ceiling(pairsInWindow * SameTokenShareOfAList);
            var samePairsBefore = new int[tokenHashes.Length + 1];
            for (var index = 0; index < tokenHashes.Length; index++)
            {
                var same = index + pattern < tokenHashes.Length && tokenHashes[index] == tokenHashes[index + pattern];
                samePairsBefore[index + 1] = samePairsBefore[index] + (same ? 1 : 0);
            }
            for (var start = 0; start < windowCount; start++)
            {
                if (samePairsBefore[start + pairsInWindow] - samePairsBefore[start] >= neededPairs) lists[start] = true;
            }
        }
        return lists;
    }

    private static ulong[] WindowHashes(ulong[] tokenHashes, int windowLength)
    {
        if (tokenHashes.Length < windowLength) return [];
        var highestPower = 1UL;
        for (var step = 1; step < windowLength; step++) highestPower *= HashBase;

        var hashes = new ulong[tokenHashes.Length - windowLength + 1];
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
