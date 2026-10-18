using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>The hold and place a cell's Location chain gives, as the load-order winners name them.</summary>
public sealed class LocationNameReaderTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Areas.esp");

    private static readonly FormKey HoldKeyword = TestRecordKeys.HoldKeyword.ToFormKey();

    private readonly SkyrimMod _mod = new(Mod, SkyrimRelease.SkyrimSE);
    private uint _nextId = 0x800;

    [Fact]
    public void TheHoldIsTheLocationBelowTheWorldspacesTopLocation()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        var pale = AddLocation("The Pale", tamriel);
        var farm = AddLocation("Loreius Farm", pale);
        var world = AddWorldspace(tamriel);

        Assert.Equal(new LocationNames("The Pale", "Loreius Farm"), Read(CellAt(farm), world));
    }

    [Fact]
    public void AChainThatEndsAtTheWorldspacesTopIsItsOwnHold()
    {
        var solstheim = AddLocation("Solstheim", parent: null);
        var world = AddWorldspace(solstheim);

        Assert.Equal(new LocationNames("Solstheim", "Solstheim"), Read(CellAt(solstheim), world));
    }

    [Fact]
    public void ATopLocationWithTheHoldKeywordIsTheHoldOfThePlacesUnderIt()
    {
        var solstheim = AddLocation("Solstheim", parent: null, isHold: true);
        var ravenRock = AddLocation("Raven Rock", solstheim);
        var world = AddWorldspace(solstheim);

        Assert.Equal(new LocationNames("Solstheim", "Raven Rock"), Read(CellAt(ravenRock), world));
    }

    [Fact]
    public void TheNearestLocationWithTheHoldKeywordIsTheHoldEvenWhenItIsInTheMiddleOfTheChain()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        var pale = AddLocation("The Pale", tamriel, isHold: true);
        var region = AddLocation("Frozen Region", pale);
        var farm = AddLocation("Loreius Farm", region);

        Assert.Equal(new LocationNames("The Pale", "Loreius Farm"), Read(CellAt(farm), worldspace: null));
    }

    [Fact]
    public void ACellWhoseOwnLocationHasTheHoldKeywordIsItsOwnHold()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        var rift = AddLocation("The Rift", tamriel, isHold: true);

        Assert.Equal(new LocationNames("The Rift", "The Rift"), Read(CellAt(rift), worldspace: null));
    }

    [Fact]
    public void AnInteriorsHoldIsTheLocationBelowTheEndOfItsChain()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        var hold = AddLocation("Whiterun Hold", tamriel);
        var city = AddLocation("Whiterun", hold);

        Assert.Equal(new LocationNames("Whiterun Hold", "Whiterun"), Read(CellAt(city), worldspace: null));
    }

    [Fact]
    public void ALocationWithoutADisplayNameIsNamedByItsEditorId()
    {
        var tamriel = AddLocation("Tamriel", parent: null);
        var pale = AddLocation(null, tamriel, editorId: "PaleHoldLocation");
        var farm = AddLocation(null, pale, editorId: "LoreiusFarmLocation");

        Assert.Equal(new LocationNames("PaleHoldLocation", "LoreiusFarmLocation"), Read(CellAt(farm), worldspace: null));
    }

    [Fact]
    public void AMissingParentEndsTheChain()
    {
        var farm = AddLocation("Loreius Farm", parent: null);
        farm.ParentLocation.SetTo(new FormKey(Mod, 0xFFF));

        Assert.Equal(new LocationNames("Loreius Farm", "Loreius Farm"), Read(CellAt(farm), worldspace: null));
    }

    [Fact]
    public void AParentLoopEndsTheChainAtTheLastLocationNotYetSeen()
    {
        var first = AddLocation("First", parent: null);
        var second = AddLocation("Second", first);
        first.ParentLocation.SetTo(second.FormKey);

        Assert.Equal(new LocationNames("First", "First"), Read(CellAt(first), worldspace: null));
    }

    [Fact]
    public void ACellWithoutALocationOrWithAMissingOneHasNoNames()
    {
        var missing = new Cell(NextKey(), SkyrimRelease.SkyrimSE);
        missing.Location.SetTo(new FormKey(Mod, 0xFFE));

        Assert.Null(Read(new Cell(NextKey(), SkyrimRelease.SkyrimSE), worldspace: null));
        Assert.Null(Read(missing, worldspace: null));
    }

    [Fact]
    public void APlaceWithoutAnyNameLeavesTheCellUnnamed()
    {
        var unnamed = AddLocation(null, parent: null);

        Assert.Null(Read(CellAt(unnamed), worldspace: null));
    }

    /// <summary>Reads the cell as the only winning cell of its space: the worldspace given, else an interior of its own.</summary>
    private LocationNames? Read(Cell cell, FormKey? worldspace)
    {
        var space = worldspace is { } worldspaceKey
            ? new SpaceFact(worldspaceKey.ToRecordKey(), EditorId: null, SpaceKind.Worldspace, Name: null)
            : new SpaceFact(cell.FormKey.ToRecordKey(), EditorId: null, SpaceKind.Interior, Name: null);
        var areas = CellAreaReader.Read([new SpaceCell(cell.FormKey.ToRecordKey(), space, cell)], new LocationNameReader(_mod.ToImmutableLinkCache(), HoldKeyword), new Execution(1));
        return Assert.Single(areas).Value.Location;
    }

    private Location AddLocation(string? name, Location? parent, string? editorId = null, bool isHold = false)
    {
        var location = new Location(NextKey(), SkyrimRelease.SkyrimSE) { Name = name, EditorID = editorId };
        if (parent is not null) location.ParentLocation.SetTo(parent.FormKey);
        if (isHold) (location.Keywords ??= []).Add(new FormLink<IKeywordGetter>(HoldKeyword));
        _mod.Locations.Add(location);
        return location;
    }

    private FormKey AddWorldspace(Location location)
    {
        var worldspace = new Worldspace(NextKey(), SkyrimRelease.SkyrimSE);
        worldspace.Location.SetTo(location.FormKey);
        _mod.Worldspaces.Add(worldspace);
        return worldspace.FormKey;
    }

    private Cell CellAt(Location location)
    {
        var cell = new Cell(NextKey(), SkyrimRelease.SkyrimSE);
        cell.Location.SetTo(location.FormKey);
        return cell;
    }

    private FormKey NextKey() => new(Mod, _nextId++);
}
