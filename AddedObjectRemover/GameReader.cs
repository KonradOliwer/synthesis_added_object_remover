using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>The edge to the game's records: reads the load order and writes the patch.</summary>
internal interface IGameReader
{
    ModFacts ReadModFacts();

    GameSnapshot Read(ModFacts mods, ModStanding standing, ReadPlan plan);

    WriteSummary Write(PatchPlan plan, RecordHandles handles);
}

internal sealed class GameReader(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) : IGameReader
{
    public ModFacts ReadModFacts() => ModFactsReader.Read(state);

    public GameSnapshot Read(ModFacts mods, ModStanding standing, ReadPlan plan) =>
        PlacedRecordScanner.Read(state, mods.Mods.KeyOf(standing.Target), plan, mods.Mods, standing);

    public WriteSummary Write(PatchPlan plan, RecordHandles handles)
    {
        var overrides = new PlacedOverrideWriter(state.PatchMod);
        var remover = new ObjectRemover(overrides);
        var enableParentsReplaced = 0;
        foreach (var id in plan.Remove)
        {
            if (remover.Disable(handles.RecordOf(id), handles.LocationOf(id))) enableParentsReplaced++;
        }

        var mover = new ObjectMover(overrides);
        foreach (var move in plan.Move)
        {
            var id = new TargetId(move.Evaluation.TargetIndex);
            mover.MoveTo(handles.RecordOf(id), handles.LocationOf(id), move.To);
        }
        return new WriteSummary(plan.Remove.Length, plan.Move.Length, enableParentsReplaced);
    }
}
