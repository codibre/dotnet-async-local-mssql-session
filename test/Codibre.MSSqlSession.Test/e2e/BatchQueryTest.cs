using Codibre.MSSqlSession.Extensions;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Codibre.MSSqlSession.Test.e2e;
public class BatchQueryTest
{
    private readonly HostApplicationBuilder _builder;
    private readonly IAsyncDbSession _target;
    private readonly ILogger<IAsyncDbSession> _logger;
    public BatchQueryTest()
    {
        _builder = Host.CreateApplicationBuilder();
        _builder.Services.AddAsyncDb();
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();
        _logger = Substitute.For<ILogger<IAsyncDbSession>>();
        _ = _builder.Services.AddSingleton<IConfiguration>(config);
        _ = _builder.Services.AddSingleton(_logger);
        var app = _builder.Build();
        _target = app.Services.GetRequiredService<IAsyncDbSession>();
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.RunInTransaction))]
    public async Task Should_Run_Transaction_Script()
    {
        // Arrange
        var totalRepetitions = 100;
        var code = 123;
        var count = 0;

        // Act
        await _target.BatchQuery.RunInTransaction(async (bs) =>
        {
            for (var i = 0; i < totalRepetitions; i++)
            {
                await bs.AddTransactionScript(@$"SELECT *
                    FROM TB_PESSOA
                    WHERE CD_PESSOA = {code}");
                count++;
            }
        });

        // Assert
        _ = count.Should().Be(totalRepetitions);
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.RunInTransaction))]
    public async Task Should_Run_Transaction_Script_TooBigForOneRoundTrip()
    {
        // Arrange
        var totalRepetitions = 3000;
        var code = 123;
        var count = 0;

        // Act
        await _target.BatchQuery.RunInTransaction(async (bs) =>
        {
            for (var i = 0; i < totalRepetitions; i++)
            {
                await bs.AddTransactionScript(@$"SELECT *
                    FROM TB_PESSOA
                    WHERE CD_PESSOA = {code}");
                count++;
            }
        });

        // Assert
        _ = count.Should().Be(totalRepetitions);
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.QueryFirstHook))]
    public async Task QueryFirstHook_Should_Return_First_ElementOfResult()
    {
        using (await _target.StartTransaction())
        {
            // Arrange
            _ = await _target.Connection.ExecuteAsync("INSERT INTO TB_PESSOA(CD_PESSOA) VALUES (1)");

            // Act
            var hook = _target.BatchQuery.QueryFirstHook<int>($"SELECT TOP 1 CD_PESSOA FROM TB_PESSOA");
            await _target.BatchQuery.RunQueries();
            var result = hook.Result;
            await _target.Rollback();

            // Assert
            _ = result.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.QueryFirstHook))]
    public async Task QueryFirstHook_Should_ThrownAnError_When_NoRecord_IsFound()
    {
        // Arrange
        Exception? thrownException = null;

        // Act
        try
        {
            _ = _target.BatchQuery.QueryFirstHook<int>($"SELECT TOP 1 CD_PESSOA FROM TB_PESSOA");
            await _target.BatchQuery.RunQueries();
        }
        catch (Exception err)
        {
            thrownException = err;
        }

        // Assert
        _ = thrownException.Should().NotBeNull();
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.QueryFirstOrDefaultHook))]
    public async Task QueryFirstOrDefaultHook_Should_Return_First_ElementOfResult()
    {
        using (await _target.StartTransaction())
        {
            // Arrange
            _ = await _target.Connection.ExecuteAsync("INSERT INTO TB_PESSOA(CD_PESSOA) VALUES (1)");

            // Act
            var hook = _target.BatchQuery.QueryFirstOrDefaultHook<int>($"SELECT TOP 1 CD_PESSOA FROM TB_PESSOA");
            await _target.BatchQuery.RunQueries();
            var result = hook.Result;
            await _target.Rollback();

            // Assert
            _ = result.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.QueryFirstOrDefaultHook))]
    public async Task QueryFirstOrDefaultHook_Should_Return_Null_WhenNoElement_IsFound()
    {
        // Arrange
        // Act
        var hook = _target.BatchQuery.QueryFirstOrDefaultHook<int?>($"SELECT TOP 1 CD_PESSOA FROM TB_PESSOA");
        await _target.BatchQuery.RunQueries();
        var result = hook.Result;
        await _target.Rollback();

        // Assert
        _ = result.Should().Be(null);
    }

    [Fact]
    [Trait("method", nameof(IBatchQuery.QueryHook))]
    public async Task QueryHook_Should_Return_Results()
    {
        using (await _target.StartTransaction())
        {
            // Arrange
            _target.BatchQuery.AddNoResultScript($"INSERT INTO TB_PESSOA(CD_PESSOA) VALUES (1)");
            _target.BatchQuery.AddNoResultScript($"INSERT INTO TB_PESSOA(CD_PESSOA) VALUES (2)");
            _target.BatchQuery.AddNoResultScript($"INSERT INTO TB_PESSOA(CD_PESSOA) VALUES (3)");
            await _target.BatchQuery.Execute();

            // Act
            var hook = _target.BatchQuery.QueryHook<int>($"SELECT TOP 3 CD_PESSOA FROM TB_PESSOA ORDER BY CD_PESSOA");
            await _target.BatchQuery.RunQueries();
            var result = hook.Result.ToList();
            await _target.Rollback();

            // Assert
            _ = result.Should().BeEquivalentTo(new List<int>() {
                1, 2, 3
            });
        }
    }
}