using Codibre.MSSqlSession.Extensions;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Codibre.MSSqlSession.Test.e2e;
public class AsyncDbSessionTest
{
    private readonly HostApplicationBuilder _builder;
    private readonly IAsyncDbSession _target;
    private readonly ILogger<IAsyncDbSession> _logger;
    public AsyncDbSessionTest()
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
    public async Task Should_Accept_Transaction_Usage_With_Dapper()
    {
        using (await _target.StartTransaction())
        {
            // Arrange
            Exception? thrownException = null;

            // Act
            try
            {
                await _target.Connection.QueryFirstOrDefaultAsync("SELECT TOP 1 * FROM TB_PRODUTO");
            }
            catch (Exception err)
            {
                thrownException = err;
            }
            await _target.Rollback();

            // Assert
            _ = thrownException.Should().BeNull();
        }
    }
}