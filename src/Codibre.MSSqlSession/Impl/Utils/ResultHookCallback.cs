namespace Codibre.MSSqlSession.Impl.Utils;

internal class ResultHookCallback<T> : IResultHook<T>
{
    private readonly Func<T> _result;
    public T Result => _result();

    internal ResultHookCallback(Func<T> result) => _result = result;
}