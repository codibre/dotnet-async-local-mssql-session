namespace Codibre.MSSqlSession.Impl.Utils;

internal class TransactionControl
{
    public bool Active { get; private set; } = false;
    public bool Open { get; set; } = false;
    public bool Cancelled { get; set; } = false;
    public bool WithHooks { get; set; } = false;
    private RunInTransactionOptions? _options = null;
    public RunInTransactionOptions Options
    {
        get
        {
            if (!Active) throw new InvalidOperationException("Transaction not activated");
            return _options!;
        }
    }

    public IDisposable Activate(RunInTransactionOptions? options)
    {
        if (Active) throw new InvalidOperationException("RunInTransaction Already called");
        options ??= new();
        Active = true;
        Open = false;
        Cancelled = false;
        WithHooks = false;
        _options = options;
        return new CallbackDisposer(() =>
        {
            Active = false;
            Open = false;
            Cancelled = false;
            WithHooks = false;
            _options = null;
        });
    }

    internal void ValidateInTransaction()
    {
        if (!Active) throw new InvalidOperationException("Must run inside RunInTransaction callback");
    }
}