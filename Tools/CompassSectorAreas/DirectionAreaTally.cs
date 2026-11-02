using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// Adds up the ground area and the number of the boxes around a point per direction: in all, and in each category the caller
/// numbers from 0. A box may be in no category; it then counts only in all.
/// </summary>
public sealed class DirectionAreaTally
{
    private readonly float[] _total = new float[CompassDirections.SectorCount];
    private readonly int[] _boxes = new int[CompassDirections.SectorCount];

    /// <summary>By category, then per direction.</summary>
    private readonly float[][] _categoryAreas;

    /// <summary>By category, then per direction.</summary>
    private readonly int[][] _categoryBoxes;

    public DirectionAreaTally(int categoryCount)
    {
        _categoryAreas = [.. Enumerable.Range(0, categoryCount).Select(_ => new float[CompassDirections.SectorCount])];
        _categoryBoxes = [.. Enumerable.Range(0, categoryCount).Select(_ => new int[CompassDirections.SectorCount])];
    }

    /// <summary>The areas of the boxes added in the given order.</summary>
    public static DirectionAreas Of(int categoryCount, IEnumerable<(SectorFootprint Footprint, int? Category)> boxesInOrder)
    {
        var tally = new DirectionAreaTally(categoryCount);
        foreach (var (footprint, category) in boxesInOrder) tally.Add(footprint, category);
        return tally.Build();
    }

    public void Add(SectorFootprint footprint, int? category)
    {
        foreach (var sector in footprint.Sectors) Add(sector, footprint.GroundArea, category);
    }

    public void Add(DirectionSector sector, float area, int? category)
    {
        var index = (int)sector;
        _total[index] += area;
        _boxes[index]++;
        if (category is { } inCategory) AddToCategory(inCategory, index, area);
    }

    /// <summary>
    /// The areas of a tally that holds every box of this one, the boxes in <paramref name="categorizedInOrder"/> in their categories
    /// and the others in none. This tally supplies only the totals, so it holds no box in a category; it is not changed. The
    /// category sums start at zero and take the boxes in the given order, exactly as <see cref="Add(SectorFootprint, int?)"/> adds
    /// them, so the result is bit for bit that of a tally that added every box in the same order.
    /// </summary>
    public DirectionAreas WithCategories(IEnumerable<(SectorFootprint Footprint, int? Category)> categorizedInOrder)
    {
        RequireNoBoxInACategory();
        var withCategories = new DirectionAreaTally(_categoryAreas.Length);
        _total.CopyTo(withCategories._total, 0);
        _boxes.CopyTo(withCategories._boxes, 0);
        foreach (var (footprint, category) in categorizedInOrder)
        {
            if (category is not { } inCategory) continue;
            foreach (var sector in footprint.Sectors) withCategories.AddToCategory(inCategory, (int)sector, footprint.GroundArea);
        }
        return withCategories.Build();
    }

    public DirectionAreas Build() =>
        new([.. _total], [.. _boxes], [.. _categoryAreas.Select(areas => areas.ToImmutableArray())], [.. _categoryBoxes.Select(boxes => boxes.ToImmutableArray())]);

    private void RequireNoBoxInACategory()
    {
        if (_categoryBoxes.Any(boxes => boxes.Any(count => count > 0)))
            throw new InvalidOperationException("The tally holds boxes in a category, so it has no totals with every box in none.");
    }

    private void AddToCategory(int category, int index, float area)
    {
        _categoryAreas[category][index] += area;
        _categoryBoxes[category][index]++;
    }
}
