namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

public enum SupportCategory { RemovedTarget, KeptTarget, OtherPlugin, Terrain }

public readonly record struct SupporterShare(Supporter Supporter, SupportCategory Category, float Share);

public static class SupporterShares
{
    public static float TotalOf(IEnumerable<SupporterShare> shares, SupportCategory category) =>
        shares.Where(share => share.Category == category).Sum(share => share.Share);
}

/// <summary>One candidate's support as a round judged it.</summary>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Proposed">Its removed share reached the threshold, so the round proposed its removal.</param>
public sealed record SupportEvaluation(int TargetIndex, CandidateContacts Contacts, IReadOnlyList<SupporterShare> Shares, bool Proposed)
{
    public float RemovedShare => ShareOf(SupportCategory.RemovedTarget);

    public float ShareOf(SupportCategory category) => SupporterShares.TotalOf(Shares, category);

    /// <summary>Shares are sorted largest first, and a proposed candidate always has a removed supporter.</summary>
    public int MainRemovedSupporter => Shares.First(share => share.Category == SupportCategory.RemovedTarget).Supporter.Index;
}
