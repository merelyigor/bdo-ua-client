using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class UpdateButtonStateTests
{
    [Fact]
    public void NoCandidate_HiddenDisabled()
    {
        var state = UpdateButtonState.Compute(null, operationActive: false);
        Assert.False(state.Visible);
        Assert.False(state.Enabled);
    }

    [Fact]
    public void Candidate_Idle_VisibleEnabled()
    {
        var candidate = MakeCandidate("v0.1.4");
        var state = UpdateButtonState.Compute(candidate, operationActive: false);
        Assert.True(state.Visible);
        Assert.True(state.Enabled);
        Assert.Equal("Оновити до v0.1.4", state.Text);
    }

    [Fact]
    public void Candidate_OperationActive_VisibleDisabled()
    {
        var candidate = MakeCandidate("v0.1.4");
        var state = UpdateButtonState.Compute(candidate, operationActive: true);
        Assert.True(state.Visible);
        Assert.False(state.Enabled);
        Assert.Equal("Оновити до v0.1.4", state.Text);
    }

    [Fact]
    public void Candidate_OperationEnds_VisibleEnabled()
    {
        var candidate = MakeCandidate("v0.1.4");
        var stateDuring = UpdateButtonState.Compute(candidate, operationActive: true);
        var stateAfter = UpdateButtonState.Compute(candidate, operationActive: false);
        Assert.True(stateDuring.Visible);
        Assert.False(stateDuring.Enabled);
        Assert.True(stateAfter.Visible);
        Assert.True(stateAfter.Enabled);
    }

    private static UpdateCandidate MakeCandidate(string tag)
    {
        return new UpdateCandidate(
            AppVersion.TryParseReleaseTag(tag)!.Value,
            tag,
            new GitHubRelease { TagName = tag });
    }
}
