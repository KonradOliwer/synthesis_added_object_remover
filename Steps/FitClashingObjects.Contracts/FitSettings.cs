namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <param name="ShrinkLimitPercent">An object may be shrunk down to this share of its size, at least 1; 100 or more: it is never shrunk.</param>
/// <param name="TurnLimitDegrees">An object may be turned about its up axis by up to this many degrees either way, at least 0; 0: it is never turned; 180 or more: to any heading.</param>
public sealed record FitSettings(int ShrinkLimitPercent, int TurnLimitDegrees);
