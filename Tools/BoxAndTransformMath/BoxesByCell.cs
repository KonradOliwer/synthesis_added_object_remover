namespace AddedObjectRemover;

/// <summary>Boxes by space, found through the exterior cells their footprints seen from above reach (<see cref="ExteriorGrid.CellsUnder"/>).</summary>
/// <param name="boxes">The boxes, each with its space; a box's position in this list is its position in every answer.</param>
public sealed class BoxesByCell<TSpace>(IReadOnlyList<(TSpace Space, Box Box)> boxes) where TSpace : notnull
{
    private readonly ILookup<(TSpace Space, int X, int Y), int> _byCell = Enumerable.Range(0, boxes.Count)
        .SelectMany(position => ExteriorGrid.CellsUnder(boxes[position].Box).Select(cell => (Cell: (boxes[position].Space, cell.X, cell.Y), Position: position)))
        .ToLookup(entry => entry.Cell, entry => entry.Position);

    public bool IsEmpty => _byCell.Count == 0;

    public Box BoxAt(int position) => boxes[position].Box;

    /// <summary>The positions of the boxes of the space that share a cell with the box, ascending, each once; they need not overlap it.</summary>
    public IEnumerable<int> SharingACellWith(TSpace space, Box box) =>
        ExteriorGrid.CellsUnder(box).SelectMany(cell => _byCell[(space, cell.X, cell.Y)]).Distinct().Order();
}
