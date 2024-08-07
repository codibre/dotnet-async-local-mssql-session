﻿using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Codibre.MSSqlSession.Extensions;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Codibre.MSSqlSession.Test.e2e;
public class AsyncScopeTest
{
    private readonly HostApplicationBuilder _builder;
    private IAsyncDbSession? _pivot = null;
    private readonly ConcurrentDictionary<DbTransaction, byte> _transactions1 = [];
    private readonly ConcurrentDictionary<DbTransaction, byte> _transactions2 = [];
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IConfigurationRoot _config;
    public AsyncScopeTest(ITestOutputHelper testOutputHelper)
    {
        _builder = Host.CreateApplicationBuilder();
        _builder.Services.AddAsyncDb();
        _testOutputHelper = testOutputHelper;
        _config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();
        _builder.Services.AddSingleton<IConfiguration>(_config);
    }

    internal async Task Run(IAsyncDbSession session)
    {
        var watch = Stopwatch.StartNew();
        _pivot = session;
        var tasks = new List<Task>();
        var i = 0;
        while (i < 10)
        {
            i++;
            tasks.Add(RunSubTask(session, i, _transactions1));
        }
        await Task.WhenAll(tasks);
        i = 0;
        tasks.Clear();
        while (i < 10)
        {
            i++;
            tasks.Add(RunSubTask(session, i, _transactions2));
        }
        await Task.WhenAll(tasks);
        _testOutputHelper.WriteLine($"Finished in {watch.ElapsedMilliseconds}ms");
    }

    internal async Task RunSubTask(
        IAsyncDbSession session,
        int count,
        ConcurrentDictionary<DbTransaction, byte> transactions
    )
    {
        await session.Run(async () =>
        {
            if (_pivot != session) throw new Exception("Different session!");
            _testOutputHelper.WriteLine($"{count} START");
            if (session.Transaction is not null) throw new Exception("Scope invasion!");
            await session.Connection.QueryAsync("SELECT TOP 1 * FROM TB_PRODUTO");
            _testOutputHelper.WriteLine($"{count} CONNECTED");
            await session.StartTransaction();
            if (session.Transaction is null) throw new Exception("Transaction not created!");
            transactions.TryAdd(session.Transaction, 0);
            await session.Connection.QueryAsync("SELECT TOP 1 * FROM TB_PRODUTO");
            _testOutputHelper.WriteLine($"{count} TRANSACTION");
            await Task.Delay(5000);
            await session.Rollback();
            _testOutputHelper.WriteLine($"{count} FINISHED");
        });
    }

    [Theory]
    [InlineData("False")]
    [InlineData("True")]
    public async Task Should_Keep_Separate_Scopes_For_Different_Tasks(string customPool)
    {
        // Arrange
        _config.GetSection("SqlConfig").GetSection("CustomPool").Value = customPool;
        var host = _builder.Build();
        var session = host.Services.GetRequiredService<IAsyncDbSession>();

        // Act
        await Run(session);

        // Assert
        _transactions1.Count.Should().BeGreaterThan(8);
        _transactions2.Count.Should().BeGreaterThan(8);
    }

    [Theory]
    [InlineData("False")]
    [InlineData("True")]
    public async Task Should_Keep_Same_Connection_Through_Task_Flow(string customPool)
    {
        // Arrange
        _config.GetSection("SqlConfig").GetSection("CustomPool").Value = customPool;
        var host = _builder.Build();
        var session = host.Services.GetRequiredService<IAsyncDbSession>();
        DbConnection? connection1 = null;
        DbConnection? connection2 = null;
        DbConnection? connection3 = null;

        // Act
        await session.Run(async () =>
        {
            ;
            connection1 = session.Connection;
            await connection1.QueryAsync("SELECT TOP 1 * FROM TB_PRODUTO");
            connection2 = session.Connection;
            await connection2.QueryAsync("SELECT TOP 1 * FROM TB_PESSOA");
            connection3 = session.Connection;
            await connection3.QueryAsync("SELECT TOP 1 * FROM TB_PEDIDO");
        });

        // Assert
        connection1.Should().Be(connection2);
        connection1.Should().Be(connection3);
    }

    [Theory]
    [InlineData("False")]
    [InlineData("True")]
    public async Task Should_Use_Different_Connections_For_Sequential_RunCalls(string customPool)
    {
        // Arrange
        _config.GetSection("SqlConfig").GetSection("CustomPool").Value = customPool;
        var host = _builder.Build();
        var session = host.Services.GetRequiredService<IAsyncDbSession>();
        DbConnection? connection1 = null;
        DbConnection? connection2 = null;
        DbConnection? connection3 = null;

        // Act
        await session.Run(async () =>
        {
            connection1 = session.Connection;
            await connection1.QueryAsync("SELECT TOP 1 * FROM TB_PRODUTO");
        });
        await session.Run(async () =>
        {
            connection2 = session.Connection;
            await connection2.QueryAsync("SELECT TOP 1 * FROM TB_PESSOA");
        });
        await session.Run(async () =>
        {
            connection3 = session.Connection;
            await connection3.QueryAsync("SELECT TOP 1 * FROM TB_PEDIDO");
        });

        // Assert
        connection1.Should().NotBe(connection2);
        connection1.Should().NotBe(connection3);
        connection2.Should().NotBe(connection3);
    }

    [Theory]
    [InlineData("False")]
    [InlineData("True")]
    public async Task Should_Create_Transient_Connections_When_Trying_To_Query_Outside_RunCall(string customPool)
    {
        // Arrange
        _config.GetSection("SqlConfig").GetSection("CustomPool").Value = customPool;
        var host = _builder.Build();
        var session = host.Services.GetRequiredService<IAsyncDbSession>();
        DbConnection? connection = null;
        var inScopeSameConnection = false;
        object? result1 = null;

        // Act
        await Task.Run(async () =>
        {
            connection = session.Connection;
            result1 = await connection.QueryAsync("SELECT TOP 1 * FROM TB_PRODUTO");
            inScopeSameConnection = connection == session.Connection;
        });
        var connection2 = session.Connection;
        var result2 = await session.Connection.QueryAsync("SELECT TOP 1 * FROM TB_PRODUTO");

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        connection.Should().NotBe(connection2);
        inScopeSameConnection.Should().BeTrue();
        connection2.Should().Be(session.Connection);
        connection!.State.Should().Be(ConnectionState.Closed);
        connection2.State.Should().Be(ConnectionState.Closed);
    }
}