namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Runs the composition on the fixture load order, in a temporary folder of its own, and returns what it decided.</summary>
internal static class FixtureRun
{
    public static Outcome Execute(Settings settings, int workers)
    {
        var root = Directory.CreateTempSubdirectory("aor-run-");
        try
        {
            var dataFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var world = FixtureWorld.Create(dataFolder);
            var state = FixturePatcherState.Create(
                world.LoadOrder, dataFolder, Path.Combine(root.FullName, FixturePatcherState.PatchModKey.FileName));
            var game = new GameReader(state);
            var mods = game.ReadModFacts();
            var built = OptionsBuilder.Build(settings, mods, state.OutputPath.Path, workers);
            var options = ((OptionsResult.Ready)built).Options;
            var assets = Assets.Create(state, new BaseFactsReader(state.LinkCache));
            return ((RunResult.Done)Composition.Run(options, mods, game, assets, new SilentRunLog())).Outcome;
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class SilentRunLog : IRunLog
    {
        public void Print(LogSection section)
        {
        }

        public T Timed<T>(Func<T> work, Func<T, TimeSpan, LogSection> section) => work();
    }
}
