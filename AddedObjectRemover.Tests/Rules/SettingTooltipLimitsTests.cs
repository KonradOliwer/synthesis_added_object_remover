using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// A tooltip is a constant text, so a limit it states as a number is written a second time next to the validator's constant.
/// Each stated limit is pinned here to that constant, and a tooltip with a range (such as 0-256) that is not listed fails.
/// </summary>
public class SettingTooltipLimitsTests
{
    private const string TooltipAttributeName = "SynthesisTooltip";

    private static readonly Regex RangeInText = new(@"\b\d+-\d+\b");

    /// <summary>The property and the limit its tooltip states, as the validator's constants give it.</summary>
    private static readonly (Type Owner, string Property, string Stated)[] StatedLimits =
    [
        (typeof(TerrainChangeSettings), nameof(TerrainChangeSettings.HeightChangeTolerance), Range(0, CollisionSettingsBuilder.MaxHeightChangeTolerance)),
        (typeof(TerrainChangeSettings), nameof(TerrainChangeSettings.MinTerrainTypeChangePercent), Range(0, Percent.PerWhole)),
        (typeof(TerrainChangeSettings), nameof(TerrainChangeSettings.WaterHeightChangeTolerance), Range(0, CollisionSettingsBuilder.MaxHeightChangeTolerance)),
        (typeof(TerrainChangeSettings), nameof(TerrainChangeSettings.MinWaterCoverChangePercent), Range(0, Percent.PerWhole)),
        (typeof(MarginBySizeSettings), nameof(MarginBySizeSettings.SizeMultiplier), Range(0, CollisionSettingsBuilder.MaxSizeMultiplier)),
        (typeof(MarginByDistanceSettings), nameof(MarginByDistanceSettings.DistanceInUnits), Range(0, CollisionSettingsBuilder.MaxMarginInUnits)),
        (typeof(ShrinkOrTurnSettings), nameof(ShrinkOrTurnSettings.ShrinkLimitPercent), $"At least {CollisionSettingsBuilder.MinShrinkLimitPercent}"),
        (typeof(ChangedObjectCollisionSettings), nameof(ChangedObjectCollisionSettings.ShrinkLimitPercent), $"At least {CollisionSettingsBuilder.MinShrinkLimitPercent}"),
        (typeof(TrustSettings), nameof(TrustSettings.MaxOtherMastersOfRequiringModToTrust), Range(0, TrustSettingsBuilder.MaxOtherMastersLimit)),
        (typeof(FollowUpRemovalSettings), nameof(FollowUpRemovalSettings.TouchDistance), Range(0, AlsoRemoveSettingsBuilder.MaxTouchDistance)),
        (typeof(FollowUpRemovalSettings), nameof(FollowUpRemovalSettings.AnchoringThresholdPercent), Range(AlsoRemoveSettingsBuilder.MinPercent, Percent.PerWhole)),
        (typeof(FollowUpRemovalSettings), nameof(FollowUpRemovalSettings.OtherModsObjectsLeftWithoutSupport), $"within {Stated(OtherModsObjectsLimits.MaxPullDistance)} units"),
        (typeof(RestoreWhereRemovedSettings), nameof(RestoreWhereRemovedSettings.TerrainEdgeBlendDistanceInUnits), Range(0, RestoreSettingsBuilder.MaxEdgeBlendDistance)),
        (typeof(ClearedAreaSettings), nameof(ClearedAreaSettings.SearchRadius), Range(RestoreSettingsBuilder.MinClearedAreaRadius, RestoreSettingsBuilder.MaxClearedAreaRadius)),
        (typeof(ClearedAreaSettings), nameof(ClearedAreaSettings.MaxStandingSizePercent), Range(0, Percent.PerWhole)),
        (typeof(ClearedAreaSettings), nameof(ClearedAreaSettings.MaxStandingCountPercent), Range(0, Percent.PerWhole)),
        (typeof(LeftoverInvisibleObjectSettings), nameof(LeftoverInvisibleObjectSettings.MoveKeptMarkersOutOfOtherModsObjects), $"up to {Stated(MarkerMoveLimits.MaxDistance)} units"),
        .. SurroundingLimits(typeof(VisibleSurroundingObjectsSettings)),
        .. SurroundingLimits(typeof(SurroundingObjectsSettings)),
    ];

    [Fact]
    public void EveryTooltipStatesTheLimitsTheValidatorHolds()
    {
        var wrong = StatedLimits
            .Select(limit => (Key: $"{limit.Owner.Name}.{limit.Property}", limit.Stated, Tooltip: TooltipOf(limit.Owner.GetProperty(limit.Property)!) ?? string.Empty))
            .Where(limit => !limit.Tooltip.Contains(limit.Stated, StringComparison.Ordinal))
            .Select(limit => $"{limit.Key} should state \"{limit.Stated}\"")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void NoTooltipStatesARangeThatIsNotPinned()
    {
        var pinned = StatedLimits.Select(limit => $"{limit.Owner.Name}.{limit.Property}").ToHashSet();

        var notPinned = typeof(Settings).Assembly.GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => TooltipOf(property) is { } tooltip && RangeInText.IsMatch(tooltip))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .Except(pinned)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(notPinned);
    }

    private static IEnumerable<(Type Owner, string Property, string Stated)> SurroundingLimits(Type owner) =>
    [
        (owner, nameof(SurroundingObjectsSettings.SearchRadius), Range(LeftBehindSettingsBuilder.MinSearchRadius, LeftBehindSettingsBuilder.MaxSearchRadius)),
        (owner, nameof(SurroundingObjectsSettings.MinOccupiedDirections), Range(LeftBehindSettingsBuilder.MinOccupiedDirectionsLowest, CompassDirections.SectorCount)),
        (owner, nameof(SurroundingObjectsSettings.DirectionThresholdPercent), WholeTensRange()),
        (owner, nameof(SurroundingObjectsSettings.ClearedDirectionsPercent), WholeTensRange()),
    ];

    private static string WholeTensRange() => $"{Range(SettingCorrections.PercentStep, Percent.PerWhole)}, in steps of {SettingCorrections.PercentStep}";

    private static string Range(float lowest, float highest) => $"{Stated(lowest)}-{Stated(highest)}";

    private static string Stated(float value) => string.Create(CultureInfo.InvariantCulture, $"{value}");

    private static string? TooltipOf(PropertyInfo property) =>
        property.CustomAttributes.SingleOrDefault(attribute => attribute.AttributeType.Name.StartsWith(TooltipAttributeName, StringComparison.Ordinal))
            ?.ConstructorArguments[0].Value as string;
}
