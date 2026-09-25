using System.Text;

namespace AddedObjectRemover;

/// <summary>Mirrors Console.Out and Console.Error into a log file while keeping normal console output.</summary>
public sealed class ConsoleLogFile : IDisposable
{
    private const string Option = "--log-file";

    private readonly StreamWriter _file;
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;

    private ConsoleLogFile(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _file = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
        var synchronizedFile = TextWriter.Synchronized(_file);
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Console.SetOut(new TeeWriter(_originalOut, synchronizedFile));
        Console.SetError(new TeeWriter(_originalError, synchronizedFile));
    }

    /// <summary>Removes "--log-file &lt;path&gt;" from <paramref name="args"/> and starts logging if present.</summary>
    public static ConsoleLogFile? TryStart(ref string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, Option, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return null;
        var path = args[index + 1];
        args = args.Take(index).Concat(args.Skip(index + 2)).ToArray();
        return new ConsoleLogFile(path);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _file.Dispose();
    }

    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;
        public override void Write(char value) { first.Write(value); second.Write(value); }
        public override void Write(string? value) { first.Write(value); second.Write(value); }
        public override void WriteLine(string? value) { first.WriteLine(value); second.WriteLine(value); }
        public override void Flush() { first.Flush(); second.Flush(); }
    }
}
