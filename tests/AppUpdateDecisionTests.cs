using LlamaServerLauncher.Services;

public static class AppUpdateDecisionTests
{
    private const string ReleasedHash = "f71ced06a41529ddd0913f54814519c29f89efcbda09ba279e8e002c13ee2a63";
    private const string OtherHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static void Run(Harness h)
    {
        h.Section("AppUpdateDecision - versions decide first");
        h.Check("newer release offered",
            AppUpdateService.Decide("v1.8", "v1.7", null, () => null) == AppUpdateVerdict.Newer, "v1.8 over v1.7");
        h.Check("older release refused even when the binary differs",
            AppUpdateService.Decide("v1.7", "v1.8", ReleasedHash, () => OtherHash) == AppUpdateVerdict.NotNewer,
            "v1.7 under v1.8");
        h.Check("same version with the same binary is nothing",
            AppUpdateService.Decide("v1.7", "v1.7", ReleasedHash, () => ReleasedHash) == AppUpdateVerdict.NotNewer,
            "v1.7 == v1.7");
        h.Check("same version rebuilt is offered",
            AppUpdateService.Decide("v1.7", "v1.7", ReleasedHash, () => OtherHash) == AppUpdateVerdict.Rebuilt,
            "republished assets under the same tag");
        h.Check("digest case ignored",
            AppUpdateService.Decide("v1.7", "v1.7", ReleasedHash.ToUpperInvariant(), () => ReleasedHash) == AppUpdateVerdict.NotNewer,
            "upper vs lower hex");

        h.Section("AppUpdateDecision - no digest (release list read off github.com pages)");
        h.Check("newer still offered without a digest",
            AppUpdateService.Decide("v1.9", "v1.8", null, () => ReleasedHash) == AppUpdateVerdict.Newer, "v1.9 over v1.8");
        h.Check("same version without a digest is nothing",
            AppUpdateService.Decide("v1.8", "v1.8", null, () => ReleasedHash) == AppUpdateVerdict.NotNewer, "v1.8 == v1.8");
        h.Check("older version without a digest is nothing",
            AppUpdateService.Decide("v1.7", "v1.8", "", () => ReleasedHash) == AppUpdateVerdict.NotNewer, "v1.7 under v1.8");

        h.Section("AppUpdateDecision - the hash is not read unless it decides");
        var hashReads = 0;
        AppUpdateService.Decide("v2.0", "v1.8", ReleasedHash, () => { hashReads++; return OtherHash; });
        h.Check("newer version does not hash the binary", hashReads == 0, $"reads={hashReads}");
        AppUpdateService.Decide("v1.7", "v1.8", ReleasedHash, () => { hashReads++; return OtherHash; });
        h.Check("older version does not hash the binary either", hashReads == 0, $"reads={hashReads}");
        AppUpdateService.Decide("v1.8", "v1.8", ReleasedHash, () => { hashReads++; return OtherHash; });
        h.Check("equal versions do hash the binary", hashReads == 1, $"reads={hashReads}");

        h.Section("AppUpdateDecision - unreadable versions");
        h.Check("unreadable tag falls back to the hash",
            AppUpdateService.Decide("nightly", "v1.8", ReleasedHash, () => OtherHash) == AppUpdateVerdict.Rebuilt, "nightly");
        h.Check("unreadable tag with a matching hash is nothing",
            AppUpdateService.Decide("nightly", "v1.8", ReleasedHash, () => ReleasedHash) == AppUpdateVerdict.NotNewer, "nightly");
        h.Check("nothing comparable at all",
            AppUpdateService.Decide("nightly", "dev", null, () => null) == AppUpdateVerdict.Unknown, "no tag, no digest");
        h.Check("unreadable local version falls back to the hash",
            AppUpdateService.Decide("v1.8", "dev", ReleasedHash, () => OtherHash) == AppUpdateVerdict.Rebuilt, "dev build");
        h.Check("hash that cannot be computed decides nothing",
            AppUpdateService.Decide("v1.8", "v1.8", ReleasedHash, () => null) == AppUpdateVerdict.NotNewer, "no local hash");
    }
}
