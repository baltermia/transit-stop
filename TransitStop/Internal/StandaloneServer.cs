using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TransitStop.Internal;

/// <summary>
/// Serves the UI from a separate, minimal Kestrel server bound to localhost, for hosts that have
/// no HTTP pipeline of their own. It starts from an empty builder so none of the app's
/// configuration (URLs, Kestrel endpoints, middleware) leaks into it.
/// </summary>
internal sealed class StandaloneServer(TransitStopRuntime runtime, TransitStopOptions options, ILogger<StandaloneServer> logger)
	: IHostedService, IAsyncDisposable
{
	WebApplication? app;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (!runtime.IsEnabled)
		{
			runtime.LogDisabled();
			return;
		}

		int port = options.StandalonePort ?? TransitStopOptions.DefaultPort;

		WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
		builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port));
		builder.Services.AddRoutingCore();

		// the app owns Ctrl+C and SIGTERM, this server is only stopped through StopAsync
		builder.Services.AddSingleton<IHostLifetime, NoLifetime>();

		WebApplication server = builder.Build();
		TransitStopEndpoints.Map(server, runtime);

		try
		{
			await server.StartAsync(cancellationToken);
		}
		catch (Exception e)
		{
			// a busy port must not take the app down with it
			logger.LogError(e, "TransitStop could not start its server on port {Port}", port);
			await server.DisposeAsync();
			return;
		}

		app = server;
		logger.LogInformation("TransitStop is available at http://localhost:{Port}/", port);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (app is not null)
			await app.StopAsync(cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		if (app is not null)
			await app.DisposeAsync();
	}

	sealed class NoLifetime : IHostLifetime
	{
		public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
