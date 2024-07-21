using Codibre.MSSqlSession.Extensions;
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
        count.Should().Be(totalRepetitions);
    }

    [Fact]
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
        count.Should().Be(totalRepetitions);
    }
}