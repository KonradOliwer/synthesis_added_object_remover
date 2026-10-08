using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Opens the reader and writer of the load order's plugin records; one per run.</summary>
public interface IPluginRecordsFactory
{
    IPluginRecords Open();
}

/// <summary>Reads the load order's plugin records and writes override records into the patch plugin. Not thread-safe, except <see cref="ReadHomeCellGrid"/>, <see cref="ReadTerrain"/>, <see cref="FindLandWorldspace"/>, <see cref="ReadNavmeshes"/>, <see cref="NpcTraitsOf"/>, <see cref="TraitSupplierOf"/>, <see cref="LeveledEntriesOf"/> and <see cref="TryParsePluginName"/>.</summary>
public interface IPluginRecords
{
    /// <summary>Walks every plugin's cells once; the records the later reads and writes need are remembered until the reader is dropped.</summary>
    PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope);

    /// <summary>
    /// The links into the given target-plugin records from every version of every record that is not a placed record, in the plugins
    /// that can link to the target plugin. The links of placed records are in <see cref="PlacedRecordFacts.Links"/>.
    /// </summary>
    ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets);

    bool HasEnableParent(RecordKey record);

    /// <summary>Writes each order as an override of the record in its winning cell; the records must have been read before.</summary>
    void Write(IReadOnlyList<WriteOrder> orders);

    /// <summary>The grid point of the winning cell whose child list holds the record; the placed records must have been read before. Thread-safe.</summary>
    /// <returns>Null where that cell has no grid: an interior cell, or the persistent cell of a worldspace.</returns>
    CellGridPoint? ReadHomeCellGrid(RecordKey record);

    /// <summary>The terrain heights of an exterior cell, row by row (<see cref="TerrainHeights.VerticesPerSide"/> per row); the placed records must have been read before.</summary>
    /// <returns>Null where the cell has no terrain (no, a deleted, or an empty land record).</returns>
    float[]? ReadTerrain(ExteriorCell cell);

    /// <summary>
    /// The worldspace whose land data a target worldspace uses (itself, or a parent whose land data it uses); the placed records
    /// must have been read before, with the terrain asked for. Thread-safe.
    /// </summary>
    /// <returns>Null where the space is not such a worldspace or the worldspace holds no land data of its own.</returns>
    RecordKey? FindLandWorldspace(RecordKey space);

    /// <summary>The winning navmesh triangles (world space) of the bucket; the placed records must have been read before.</summary>
    /// <returns>Null where the bucket has no navmesh.</returns>
    MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket);

    /// <summary>An NPC's raw record facts. Thread-safe.</summary>
    /// <returns>Null when the base is not found as an NPC.</returns>
    NpcTraits? NpcTraitsOf(BaseKey npc);

    /// <summary>
    /// The record that supplies a placed base's traits, found along the template chain by the given template flag. Thread-safe.
    /// </summary>
    /// <returns>Null when the chain breaks or loops.</returns>
    TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag);

    /// <summary>
    /// The NPCs supplying their own traits that a leveled list can spawn, through nested lists and templates, each record once, in
    /// record order. Thread-safe.
    /// </summary>
    IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag);

    /// <summary>Reads a plugin file name by the game library's rules: its accepted extensions and characters. Thread-safe.</summary>
    /// <returns>False where the text is not a valid plugin file name.</returns>
    bool TryParsePluginName(string text, out PluginName name);
}
