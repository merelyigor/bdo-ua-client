using BdoClient.Logging;
using BdoClient.Services;

namespace BdoClient.Update;

internal static class PreparedAttemptCleanup
{
    public static bool TryDeleteCandidate(UpdateSession session, ILogger logger)
    {
        var targetPath = Path.GetFullPath(session.TargetPath);
        var targetDir = Path.GetDirectoryName(targetPath);
        if (targetDir == null)
            return false;

        var candidatePath = Path.Combine(
            targetDir,
            $"{Path.GetFileName(targetPath)}.update-{session.SessionId}.new");

        if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            return true;

        if (!File.Exists(candidatePath))
        {
            logger.Warning($"Self-update: candidate is not a regular file at {candidatePath}; not deleting");
            return false;
        }

        try
        {
            var candidateSha = HashHelper.ComputeFileSha256(candidatePath);
            if (!string.Equals(candidateSha, session.StagedExeSha256, StringComparison.Ordinal))
            {
                logger.Warning($"Self-update: candidate SHA mismatch at {candidatePath}; not deleting");
                return false;
            }

            File.Delete(candidatePath);
            logger.Debug($"Self-update: deleted verified candidate {candidatePath}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"Self-update: candidate verification/cleanup failed at {candidatePath}: {ex.Message}");
            return false;
        }
    }
}
