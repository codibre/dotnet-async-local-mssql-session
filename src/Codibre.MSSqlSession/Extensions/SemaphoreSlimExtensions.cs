namespace Codibre.MSSqlSession.Extensions;

public static class SemaphoreSlimExtension
{
    public static async ValueTask WaitAll(
        this SemaphoreSlim semaphoreSlim,
        int maxConcurrency,
        CancellationToken? cancellationToken = null)
    {
        while (semaphoreSlim.CurrentCount < maxConcurrency)
        {
            await Task.Delay(10);
            if (cancellationToken?.IsCancellationRequested ?? false) break;
        }
    }
}