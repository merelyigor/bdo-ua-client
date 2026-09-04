using System;
using System.IO;

namespace BdoClient.Services;

/// <summary>
/// Cheap, content-free fingerprint of the game localization file.
/// Used by the T4 local-change monitor to detect external file replacement
/// without hashing the file on every check.
/// </summary>
internal readonly record struct LocalizationFileFingerprint
{
    public bool Exists { get; }

    public long Length { get; }

    public DateTime LastWriteTimeUtc { get; }

    public LocalizationFileFingerprint(bool exists, long length, DateTime lastWriteTimeUtc)
    {
        Exists = exists;
        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    /// <summary>
    /// Canonical fingerprint for a missing/deleted file. Exists=false, Length=0,
    /// LastWriteTimeUtc=default. This is a valid fingerprint, NOT a capture failure,
    /// so that existing -&gt; missing and missing -&gt; existing are detected as real changes.
    /// </summary>
    public static LocalizationFileFingerprint Missing => new(false, 0, default);

    /// <summary>
    /// Captures only metadata (existence, length, last-write time). Never reads file
    /// contents and never computes a SHA.
    /// </summary>
    /// <param name="filePath">Absolute localization file path.</param>
    /// <param name="fingerprint">Captured fingerprint (Missing on file-not-found / directory-not-found).</param>
    /// <param name="error">Transient capture failure reason when the method returns false; null otherwise.</param>
    /// <returns>true when a fingerprint was captured (including Missing); false on transient IO failure.</returns>
    public static bool TryCapture(string filePath, out LocalizationFileFingerprint fingerprint, out string? error)
    {
        fingerprint = default;
        error = null;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                fingerprint = Missing;
                return true;
            }

            fingerprint = new LocalizationFileFingerprint(true, info.Length, info.LastWriteTimeUtc);
            return true;
        }
        catch (FileNotFoundException)
        {
            // File disappeared between the existence check and metadata read.
            fingerprint = Missing;
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // Game root / ads directory no longer present.
            fingerprint = Missing;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
