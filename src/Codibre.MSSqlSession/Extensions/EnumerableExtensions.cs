namespace Codibre.MSSqlSession.Extensions;

public static class EnumerableExtensions
{
    /// <summary>
    /// Executes a function before starting the iteration.
    /// The function may return an IDisposable that will be
    /// disposed at the end of the iteration
    /// </summary>
    /// <typeparam name="T">Source item type</typeparam>
    /// <param name="enumerable">The source enumerable</param>
    /// <param name="onStart">The function to be executed</param>
    /// <returns>An enumerable of the same type as source</returns>
    public static async IAsyncEnumerable<T> OnStartAsync<T>(
        this IAsyncEnumerable<T> enumerable,
        Func<ValueTask<IDisposable?>> onStart
    )
    {
        using var started = await onStart();
        await foreach (var item in enumerable) yield return item;
    }

    /// <summary>
    /// Should execute the transformation for every item yielded,
    /// concurrently, respecting the maximum concurrency informed
    /// </summary>
    /// <typeparam name="T">The source item type</typeparam>
    /// <typeparam name="R">The transformation resulting type</typeparam>
    /// <param name="enumerable">The source enumerable</param>
    /// <param name="transform">The transformation to be executed</param>
    /// <param name="maxConcurrency">The maximum concurrency</param>
    /// <param name="cancellationToken">Cancellation token, optional</param>
    /// <returns></returns>
    public static async Task<List<R>> RunConcurrent<T, R>(
        this IAsyncEnumerable<T> enumerable,
        Func<T, Task<R>> transform,
        int maxConcurrency
    )
    {
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var result = new List<R>();
        var exceptions = new List<Exception>();
        await foreach (var x in enumerable)
        {
            await semaphore.WaitAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    result.Add(await transform(x));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }
        await semaphore.WaitAll(maxConcurrency);
        if (exceptions.Count > 0) throw new AggregateException(exceptions);
        return result;
    }
}