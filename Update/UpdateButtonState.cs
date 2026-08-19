namespace BdoClient.Update;

public readonly struct UpdateButtonState
{
    public bool Visible { get; }
    public bool Enabled { get; }
    public string Text { get; }

    public UpdateButtonState(bool visible, bool enabled, string text)
    {
        Visible = visible;
        Enabled = enabled;
        Text = text;
    }

    public static UpdateButtonState Compute(UpdateCandidate? candidate, bool operationActive)
    {
        if (candidate == null)
            return new UpdateButtonState(false, false, "");

        var text = $"Оновити до {candidate.TagName}";
        return new UpdateButtonState(true, !operationActive, text);
    }
}
