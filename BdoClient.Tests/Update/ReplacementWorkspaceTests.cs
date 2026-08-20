using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public sealed class ReplacementWorkspaceTests
{
    [Fact]
    public void SameVolume_UsesAppDataSessionWorkspace()
    {
        var appPaths = new AppPaths(@"C:\Users\Test\AppData\Local\BDO-UA-Client");
        var sessionId = "7ac65a9f-e591-4a56-a76b-12e849942d97";
        var workspace = ReplacementWorkspace.Derive(appPaths, sessionId, @"C:\Games\BDO-UA-Client.exe");

        Assert.False(workspace.UsesTargetVolumeFallback);
        Assert.Equal(Path.Combine(appPaths.UpdatesDir, sessionId), workspace.DirectoryPath);
        Assert.Equal(Path.Combine(workspace.DirectoryPath, "candidate.new"), workspace.CandidatePath);
        Assert.Equal(Path.Combine(workspace.DirectoryPath, "original.bak"), workspace.BackupPath);
        Assert.Equal(Path.Combine(workspace.DirectoryPath, "failed-new"), workspace.FailedNewPath);
        Assert.Equal(Path.GetPathRoot(workspace.CandidatePath), Path.GetPathRoot(@"C:\Games\BDO-UA-Client.exe"));
    }

    [Fact]
    public void CrossVolume_UsesHiddenTargetVolumeWorkspace()
    {
        var appPaths = new AppPaths(@"C:\Users\Test\AppData\Local\BDO-UA-Client");
        var sessionId = "7ac65a9f-e591-4a56-a76b-12e849942d97";
        var workspace = ReplacementWorkspace.Derive(appPaths, sessionId, @"D:\Games\BDO-UA-Client.exe");

        Assert.True(workspace.UsesTargetVolumeFallback);
        Assert.StartsWith(@"D:\Games\.bdo-ua-client-update\", workspace.DirectoryPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(sessionId, workspace.DirectoryPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetPathRoot(@"D:\Games\BDO-UA-Client.exe"), Path.GetPathRoot(workspace.CandidatePath));
        Assert.StartsWith(workspace.DirectoryPath, workspace.BackupPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(workspace.DirectoryPath, workspace.FailedNewPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionPaths_AreDeterministicAndCanonical()
    {
        var appPaths = new AppPaths(@"C:\Users\Test\AppData\Local\BDO-UA-Client");
        var first = ReplacementWorkspace.Derive(appPaths, "7ac65a9f-e591-4a56-a76b-12e849942d97", @"C:\Games\BDO-UA-Client.exe");
        var second = ReplacementWorkspace.Derive(appPaths, "7ac65a9f-e591-4a56-a76b-12e849942d97", @"C:\Games\BDO-UA-Client.exe");

        Assert.Equal(first.DirectoryPath, second.DirectoryPath);
        Assert.Equal(first.CandidatePath, second.CandidatePath);
        Assert.Equal(first.BackupPath, second.BackupPath);
        Assert.Equal(first.FailedNewPath, second.FailedNewPath);
    }

    [Fact]
    public void InvalidSessionId_IsRejectedBeforePathDerivation()
    {
        var appPaths = new AppPaths(@"C:\Users\Test\AppData\Local\BDO-UA-Client");

        Assert.Throws<ArgumentException>(() => ReplacementWorkspace.Derive(appPaths, "..", @"D:\Games\BDO-UA-Client.exe"));
    }
}
