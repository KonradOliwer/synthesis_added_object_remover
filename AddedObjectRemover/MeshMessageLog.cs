using System.Collections.Concurrent;

namespace AddedObjectRemover;

/// <summary>Verbose mesh messages from parallel phases, printed in mesh-path order so the log is deterministic.</summary>
internal sealed class MeshMessageLog(bool enabled)
{
    private readonly ConcurrentQueue<(string MeshPath, string Message)> _messages = new();

    public void Add(string meshPath, string message)
    {
        if (enabled) _messages.Enqueue((meshPath, message));
    }

    public void PrintAndClear()
    {
        var messages = new List<(string MeshPath, string Message)>();
        while (_messages.TryDequeue(out var message)) messages.Add(message);
        messages.Sort((a, b) =>
        {
            var byPath = StringComparer.OrdinalIgnoreCase.Compare(a.MeshPath, b.MeshPath);
            return byPath != 0 ? byPath : StringComparer.Ordinal.Compare(a.Message, b.Message);
        });
        foreach (var (_, message) in messages) Console.WriteLine(message);
    }
}
