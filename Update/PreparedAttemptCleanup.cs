using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient.Update;

internal static class PreparedAttemptCleanup
{
    public static bool TryDeleteCandidate(UpdateSession session, AppPaths appPaths, ILogger logger)
    {
        var workspace = ReplacementWorkspace.Derive(appPaths, session.SessionId, session.TargetPath);
        var candidatePath = workspace.CandidatePath;

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
            return workspace.TryDeleteOwnedFallbackWorkspace();
        }
        catch (Exception ex)
        {
            logger.Warning($"Self-update: candidate verification/cleanup failed at {candidatePath}: {ex.Message}");
            return false;
        }
    }
}
