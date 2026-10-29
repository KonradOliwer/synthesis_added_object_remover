namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>What a supporter is when the round judges: <see cref="RemovedTarget"/> is disabled, <see cref="RestoredTarget"/> put back to its earlier version; both have left their place.</summary>
public enum SupportCategory { RemovedTarget, RestoredTarget, KeptTarget, OtherMod, Terrain }

public readonly record struct SupporterShare(Supporter Supporter, SupportCategory Category, float Share);

public static class SupporterShares
{
    public static float TotalOf(IEnumerable<SupporterShare> shares, SupportCategory category) =>
        shares.Where(share => share.Category == category).Sum(share => share.Share);

    /// <summary>Whether the supporter is a target gone from where the cleaned mod put it, disabled or restored.</summary>
    public static bool HasLeftItsPlace(SupportCategory category) => category is SupportCategory.RemovedTarget or SupportCategory.RestoredTarget;
}

/// <summary>One candidate's support as a round judged it.</summary>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Proposed">Its left-its-place share reached the threshold, so the round proposed its removal.</param>
public sealed record SupportEvaluation(int TargetIndex, CandidateContacts Contacts, IReadOnlyList<SupporterShare> Shares, bool Proposed)
{
    /// <summary>The share held by disabled objects.</summary>
    public float RemovedShare => SupporterShares.TotalOf(Shares, SupportCategory.RemovedTarget);

    public float RestoredShare => SupporterShares.TotalOf(Shares, SupportCategory.RestoredTarget);

    /// <summary>The share held by objects gone from where the cleaned mod put them, disabled or restored: the one the threshold is measured against.</summary>
    public float LeftItsPlaceShare => Shares.Where(share => SupporterShares.HasLeftItsPlace(share.Category)).Sum(share => share.Share);

    /// <summary>Shares are sorted largest first, and a proposed candidate always has a supporter that left its place.</summary>
    public int MainLeftItsPlaceSupporter => Shares.First(share => SupporterShares.HasLeftItsPlace(share.Category)).Supporter.Index;
}
