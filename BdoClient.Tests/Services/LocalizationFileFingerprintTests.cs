using System;
using System.IO;
using BdoClient.Services;
using Xunit;

namespace BdoClient.Tests.Services;

public class LocalizationFileFingerprintTests
{
    [Fact]
    public void MissingFile_ReturnsCanonicalMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "languagedata_en.loc");

        var ok = LocalizationFileFingerprint.TryCapture(path, out var fingerprint, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(LocalizationFileFingerprint.Missing, fingerprint);
        Assert.False(fingerprint.Exists);
        Assert.Equal(0, fingerprint.Length);
        Assert.Equal(default(DateTime), fingerprint.LastWriteTimeUtc);
    }

    [Fact]
    public void ExistingFile_CapturesLength()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "languagedata_en.loc");
        File.WriteAllText(path, "abcdefghij");

        try
        {
            var ok = LocalizationFileFingerprint.TryCapture(path, out var fingerprint, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.True(fingerprint.Exists);
            Assert.Equal(10, fingerprint.Length);
            Assert.NotEqual(default(DateTime), fingerprint.LastWriteTimeUtc);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExistingFile_CapturesLastWriteTimeUtc()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "languagedata_en.loc");
        File.WriteAllText(path, "content");

        try
        {
            var past = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, past);

            var ok = LocalizationFileFingerprint.TryCapture(path, out var fingerprint, out _);

            Assert.True(ok);
            Assert.True(fingerprint.Exists);
            Assert.Equal(past, fingerprint.LastWriteTimeUtc);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EqualMetadata_FingerprintsEqual()
    {
        var a = new LocalizationFileFingerprint(true, 100, new DateTime(2021, 5, 5, 5, 5, 5, DateTimeKind.Utc));
        var b = new LocalizationFileFingerprint(true, 100, new DateTime(2021, 5, 5, 5, 5, 5, DateTimeKind.Utc));

        Assert.Equal(a, b);
    }

    [Fact]
    public void LengthChange_FingerprintChanges()
    {
        var a = new LocalizationFileFingerprint(true, 100, DateTime.UtcNow);
        var b = new LocalizationFileFingerprint(true, 101, a.LastWriteTimeUtc);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TimestampChange_FingerprintChanges()
    {
        var a = new LocalizationFileFingerprint(true, 100, new DateTime(2021, 5, 5, 5, 5, 5, DateTimeKind.Utc));
        var b = new LocalizationFileFingerprint(true, 100, new DateTime(2021, 5, 5, 5, 5, 6, DateTimeKind.Utc));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ExistingToDeleted_FingerprintChanges()
    {
        var existing = new LocalizationFileFingerprint(true, 100, DateTime.UtcNow);
        var missing = LocalizationFileFingerprint.Missing;

        Assert.NotEqual(existing, missing);
    }

    [Fact]
    public void MissingToCreated_FingerprintChanges()
    {
        var missing = LocalizationFileFingerprint.Missing;
        var created = new LocalizationFileFingerprint(true, 5, DateTime.UtcNow);

        Assert.NotEqual(missing, created);
    }

    [Fact]
    public void FileDeletedBetweenChecks_TreatedAsMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "languagedata_en.loc");
        File.WriteAllText(path, "content");

        try
        {
            File.Delete(path);

            var ok = LocalizationFileFingerprint.TryCapture(path, out var fingerprint, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(LocalizationFileFingerprint.Missing, fingerprint);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
