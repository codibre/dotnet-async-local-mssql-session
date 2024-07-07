using Codibre.MSSqlSession.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace Codibre.MSSqlSession.Extensions;

public static class AsyncDbServiceCollectionExtensions
{
    /// <summary>
    /// Inject connection manager that uses asynclocal, ideal for use
    /// with Dapper and other frameworks that depend on management
    /// Connection Manual.
    /// </summary>
    /// <param name="serviceCollection"></param>
    public static void AddAsyncDb(this IServiceCollection serviceCollection)
        => serviceCollection.AddSingleton<IAsyncDbSession, AsyncDbSession>();

    /// <summary>
    /// initializes the connection to the bank.Useful for force pool boot
    /// during the application bootstrap, and take it out of the weight of the first
    /// Requisition
    /// </summary>
    /// <param name="serviceProvider">Instance of IserviceProvider</param>
    public static async Task StartConnection(this IServiceProvider serviceProvider)
    {
        using (await serviceProvider.GetRequiredService<IAsyncDbSession>().StartSession())
        {
            // Dummy
        }
    }
}