using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace AddedObjectRemover;

/// <param name="Written">The files written, in table order.</param>
/// <param name="Failures">One message per file that could not be written, in table order.</param>
public sealed record CsvWriteResult(ImmutableArray<WrittenTable> Written, ImmutableArray<string> Failures);

public static class CsvWriter
{
    /// <summary>Creates the folder when needed and replaces an existing file; UTF-8, comma-separated, one line per row.</summary>
    /// <returns>The file with the time it took.</returns>
    public static WrittenTable Write(string folder, CsvTable table)
    {
        var timer = Stopwatch.StartNew();
        var path = Path.Combine(folder, table.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var writer = new StreamWriter(path, false, Encoding.UTF8))
        {
            writer.WriteLine(string.Join(",", table.Header));
            foreach (var row in table.Rows) writer.WriteLine(string.Join(",", row));
        }
        return new WrittenTable(table.FileName, table.Rows.Length, path, timer.Elapsed);
    }

    /// <summary>A file that cannot be written (an expected file-access failure) becomes a failure message and the rest are still written.</summary>
    /// <param name="failureMessage">The caller's wording for the table that failed.</param>
    public static CsvWriteResult WriteAll(string folder, ImmutableArray<CsvTable> tables, Func<CsvTable, Exception, string> failureMessage)
    {
        var written = ImmutableArray.CreateBuilder<WrittenTable>();
        var failures = ImmutableArray.CreateBuilder<string>();
        foreach (var table in tables)
        {
            WrittenTable? file = null;
            var failure = FileFailures.Guard(() => file = Write(folder, table), exception => failureMessage(table, exception));
            if (failure != null) failures.Add(failure);
            else written.Add(file!);
        }
        return new CsvWriteResult(written.ToImmutable(), failures.ToImmutable());
    }
}
