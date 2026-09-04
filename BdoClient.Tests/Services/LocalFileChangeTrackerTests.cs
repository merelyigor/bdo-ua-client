using System;
using BdoClient.Services;
using Xunit;

namespace BdoClient.Tests.Services;

public class LocalFileChangeTrackerTests
{
    private static readonly string PathA = @"C:\game\ads\languagedata_en.loc";
    private static readonly string PathB = @"C:\other\ads\languagedata_en.loc";

    private static LocalizationFileFingerprint F(bool exists, long length, DateTime time)
        => new(exists, length, time);

    [Fact]
    public void NoBaseline_HasChangedFalse()
    {
        var tracker = new LocalFileChangeTracker();

        Assert.False(tracker.HasChanged(PathA, F(true, 10, DateTime.UtcNow)));
        Assert.False(tracker.HasBaselineFor(PathA));
    }

    [Fact]
    public void CommitResolved_SameFingerprintUnchanged()
    {
        var tracker = new LocalFileChangeTracker();
        var f = F(true, 10, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        tracker.CommitResolved(PathA, f);

        Assert.True(tracker.HasBaselineFor(PathA));
        Assert.False(tracker.HasChanged(PathA, f));
    }

    [Fact]
    public void CommitResolved_LengthChangeDetected()
    {
        var tracker = new LocalFileChangeTracker();
        tracker.CommitResolved(PathA, F(true, 10, DateTime.UtcNow));

        Assert.True(tracker.HasChanged(PathA, F(true, 11, DateTime.UtcNow)));
    }

    [Fact]
    public void CommitResolved_TimestampChangeDetected()
    {
        var tracker = new LocalFileChangeTracker();
        var baseTime = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        tracker.CommitResolved(PathA, F(true, 10, baseTime));

        Assert.True(tracker.HasChanged(PathA, F(true, 10, baseTime.AddSeconds(1))));
    }

    [Fact]
    public void MissingExistingTransitionDetected()
    {
        var tracker = new LocalFileChangeTracker();
        tracker.CommitResolved(PathA, LocalizationFileFingerprint.Missing);

        Assert.True(tracker.HasChanged(PathA, F(true, 5, DateTime.UtcNow)));
    }

    [Fact]
    public void DifferentPath_DoesNotReuseOldBaseline()
    {
        var tracker = new LocalFileChangeTracker();
        tracker.CommitResolved(PathA, F(true, 10, DateTime.UtcNow));

        // Same content fingerprint, but a different game root must not reuse PathA baseline.
        Assert.False(tracker.HasBaselineFor(PathB));
        Assert.False(tracker.HasChanged(PathB, F(true, 10, DateTime.UtcNow)));
    }

    [Fact]
    public void Clear_RemovesBaseline()
    {
        var tracker = new LocalFileChangeTracker();
        tracker.CommitResolved(PathA, F(true, 10, DateTime.UtcNow));

        tracker.Clear();

        Assert.False(tracker.HasBaselineFor(PathA));
        Assert.False(tracker.HasChanged(PathA, F(true, 99, DateTime.UtcNow)));
    }

    [Fact]
    public void F0Commit_F1Detected_CommitF1_F2StillDetected()
    {
        var tracker = new LocalFileChangeTracker();
        var f0 = F(true, 10, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var f1 = F(true, 11, f0.LastWriteTimeUtc.AddSeconds(1));
        var f2 = F(true, 12, f1.LastWriteTimeUtc.AddSeconds(1));

        tracker.CommitResolved(PathA, f0);

        // F0 -> F1 detected and committed as F1.
        Assert.True(tracker.HasChanged(PathA, f1));
        tracker.CommitResolved(PathA, f1);

        // F2 must still be detected after F1 was committed (no silent adoption of F2).
        Assert.True(tracker.HasChanged(PathA, f2));
    }

    [Fact]
    public void WindowsPathCasing_DoesNotDuplicateBaseline()
    {
        var tracker = new LocalFileChangeTracker();
        var f = F(true, 10, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        tracker.CommitResolved(PathA, f);

        // Same path with different casing must resolve to the same baseline.
        Assert.True(tracker.HasBaselineFor(@"c:\game\ads\languagedata_en.loc"));
        Assert.False(tracker.HasChanged(@"c:\game\ads\languagedata_en.loc", f));
    }
}
