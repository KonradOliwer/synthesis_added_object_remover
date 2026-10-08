namespace AddedObjectRemover.Tests.Architecture;

/// <summary>One stretch of code that appears elsewhere too. Both copies share the key of their first window.</summary>
internal sealed record CopiedRun(string Key, string File, int FirstLine, int LastLine)
{
    public override string ToString() => $"{Key}  {File}:{FirstLine}-{LastLine}";
}

/// <summary>
/// Finds code copied between or inside files: every run of consecutive token windows whose content also occurs
/// somewhere else without overlapping. Short copies below the window length are not found.
/// </summary>
internal static class CodeCopies
{
    private const ulong HashBase = 1_099_511_628_211UL;
    private const ulong HashSeed = 14_695_981_039_346_656_037UL;
    private const int KeyDigits = 8;

    public static IReadOnlyList<CopiedRun> Find(IReadOnlyList<(string Name, string Source)> files, int windowLength)
    {
        var tokenLines = files.Select(file => CodeTokens.Significant(file.Source)).ToList();
        var windowHashes = tokenLines.Select(tokens => WindowHashes(tokens, windowLength)).ToList();
        var occurrences = OccurrencesByHash(windowHashes);

        var runs = new List<CopiedRun>();
        for (var file = 0; file < files.Count; file++)
        {
            var hashes = windowHashes[file];
            var runStart = -1;
            for (var start = 0; start <= hashes.Length; start++)
            {
                var copied = start < hashes.Length && IsCopied(occurrences[hashes[start]], file, start, windowLength);
                if (copied && runStart < 0) runStart = start;
                if (!copied && runStart >= 0)
                {
                    runs.Add(RunOf(files[file].Name, tokenLines[file], hashes[runStart], runStart, start - 1, windowLength));
                    runStart = -1;
                }
            }
        }
        return runs;
    }

    private static CopiedRun RunOf(string file, IReadOnlyList<CodeToken> tokens, ulong firstHash, int firstWindow, int lastWindow, int windowLength) =>
        new(firstHash.ToString("x16")[^KeyDigits..], file, tokens[firstWindow].Line, tokens[lastWindow + windowLength - 1].Line);

    private static bool IsCopied(List<(int File, int Start)> sameContent, int file, int start, int windowLength) =>
        sameContent.Any(other => other.File != file || Math.Abs(other.Start - start) >= windowLength);

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
