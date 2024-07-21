namespace Codibre.MSSqlSession.Impl.Utils;

internal sealed class CrossedDisposer : IDisposable
{
    private readonly IDisposable _disposable;
    private bool _disposeTriggered = false;
    public CrossedDisposer(
        IDisposable disposable
    )
    {
        _disposable = disposable;
    }

    public void Dispose()
    {
        if (_disposeTriggered) return;
        _disposeTriggered = true;
        _disposable.Dispose();
    }

    ~CrossedDisposer() => _disposable?.Dispose();
}