using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace AddedObjectRemover;

/// <summary>The optional report folder, whose files never change the results. Failing to access it is only a warning.</summary>
internal static class ReportFolder
{
    /// <summary>Not every run writes every file, so a file an earlier run left behind would look current.</summary>
    private static readonly string[] KnownFileNames =
    [
        Tables.MeshOriginsFileName,
        Tables.AnchoringFileName,
        Tables.EdgesFileName,
        Tables.ComponentsFileName,
        Tables.LeftoversFileName,
        Tables.HintsFileName,
    ];

    /// <returns>The warning line when earlier files could not be deleted; null otherwise, and always null when no files are written.</returns>
    public static string? Prepare(ReportOptions options)
    {
        if (!options.WriteFiles) return null;

        return Access(options.Folder, "deleting earlier diagnostics files", () =>
        {
            foreach (var fileName in KnownFileNames) DeleteIfPresent(Path.Combine(options.Folder, fileName));
        });
    }

    /// <param name="tables">Rows hold fields already formatted as CSV text.</param>
    public static ReportFilesResult Write(ReportOptions options, ImmutableArray<CsvTable> tables)
    {
        var written = ImmutableArray.CreateBuilder<WrittenTable>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (options.WriteFiles)
        {
            foreach (var table in tables)
            {
                var path = Path.Combine(options.Folder, table.FileName);
                var timer = Stopwatch.StartNew();
                var warning = Access(options.Folder, $"writing {table.FileName}", () => WriteCsv(path, table));
                if (warning != null) warnings.Add(warning);
                else written.Add(new WrittenTable(table.FileName, table.Rows.Length, path, timer.Elapsed));
            }
        }
        return new ReportFilesResult(written.ToImmutable(), warnings.ToImmutable());
    }

    /// <summary>Creates the folder when needed and replaces any existing file; UTF-8, comma-separated.</summary>
    private static void WriteCsv(string path, CsvTable table)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(",", table.Header));
        foreach (var row in table.Rows) writer.WriteLine(string.Join(",", row));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <returns>The warning line when the access failed; null otherwise.</returns>
    private static string? Access(string folder, string description, Action access)
    {
        try
        {
            access();
            return null;
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            return $"  Warning: {description} in {folder} failed: {ex.Message}";
        }
    }
}

/// <param name="Written">The files written, in table order.</param>
/// <param name="Warnings">One log line per file that could not be written.</param>
internal sealed record ReportFilesResult(ImmutableArray<WrittenTable> Written, ImmutableArray<string> Warnings);
