namespace Codibre.MSSqlSession.Extensions;

public static class BatchQueryExtension
{
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        IBatchQuery batchQuery,
        Func<TInput, IBatchQuery, ValueTask<TOutput>> PreRunQuery
    ) => batchQuery.PrepareEnumerable(enumerable, PreRunQuery);
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        IBatchQuery batchQuery,
        Func<TInput, IBatchQuery, TOutput> PreRunQuery
    ) => batchQuery.PrepareEnumerable(
        enumerable,
        (input, bq) => new ValueTask<TOutput>(PreRunQuery(input, bq))
    );
}