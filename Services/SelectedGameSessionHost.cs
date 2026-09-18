namespace BdoClient.Services;

/// <summary>
/// Owns the one currently active game session. Candidate sessions are created
/// outside the active slot and become owned only after an explicit commit.
/// </summary>
public sealed class SelectedGameSessionHost : IDisposable
{
    private readonly Func<GameDescriptor, BdoGameSession> _factory;
    private BdoGameSession? _current;
    private bool _disposed;

    public SelectedGameSessionHost(
        BdoGameSession initialSession,
        Func<GameDescriptor, BdoGameSession> factory)
    {
        _current = initialSession ?? throw new ArgumentNullException(nameof(initialSession));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public BdoGameSession CurrentSession
        => _current ?? throw new ObjectDisposedException(nameof(SelectedGameSessionHost));

    public BdoGameSession CreateCandidate(GameDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        return _factory(descriptor);
    }

    public BdoGameSession CommitCandidate(BdoGameSession candidate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(candidate);

        var previous = CurrentSession;
        _current = candidate;
        return previous;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var current = _current;
        _current = null;
        current?.Dispose();
    }
}
