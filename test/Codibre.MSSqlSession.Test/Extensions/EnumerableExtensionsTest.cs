using Codibre.MSSqlSession.Extensions;
using FluentAssertions;

namespace Codibre.MSSqlSession.Test.Extensions;

public class EnumerableExtensionsTest
{
    [Fact]
    [Trait("method", nameof(EnumerableExtensions.RunConcurrent))]
    public async Task Should_Return_After_EveryTask_Ended()
    {
        // Arrange
        var size = 100;
        var rnd = new Random();
        async IAsyncEnumerable<int> GetItems()
        {
            for (var i = 0; i < size; i++)
            {
                await Task.Delay(rnd.Next(1, 50));
                yield return i;
            }
        }

        // Act
        var result = await GetItems().RunConcurrent(x => Task.FromResult(x * 10), 10);

        // Assert
        result.Should().HaveCount(size);
    }

    [Fact]
    [Trait("method", nameof(EnumerableExtensions.RunConcurrent))]
    public async Task Should_Throw_AnError_When_Some_Item_Fails()
    {
        // Arrange
        var size = 100;
        var rnd = new Random();
        async IAsyncEnumerable<int> GetItems()
        {
            for (var i = 0; i < size; i++)
            {
                await Task.Delay(rnd.Next(1, 50));
                yield return i;
            }
        }
        Exception? thrownException = null;

        // Act
        try
        {
            await GetItems().RunConcurrent(x => x == size - 1
                ? throw new InvalidDataException("My Error")
                : Task.FromResult(x * 10), 10
            );
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        // Assert
        thrownException.Should().BeOfType<AggregateException>();
    }
}