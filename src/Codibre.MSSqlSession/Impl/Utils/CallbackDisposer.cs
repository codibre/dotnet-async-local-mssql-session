namespace Codibre.MSSqlSession.Impl.Utils;

internal sealed class CallbackDisposer : IDisposable
{
    private readonly Action _callback;
    public CallbackDisposer(Action callback) => _callback = callback;

    public void Dispose() => _callback();
}