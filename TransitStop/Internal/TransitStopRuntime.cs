using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TransitStop.Internal;

/// <summary>
/// The request handlers behind the API.
/// </summary>
internal sealed class TransitStopRuntime
{
	static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	readonly TransitStopOptions options;
	readonly IHostEnvironment? environment;
	readonly ILogger logger;
	readonly Lazy<MessageCatalog> catalog;

	public TransitStopRuntime(IServiceProvider services, TransitStopOptions options, ILogger<TransitStopRuntime> logger)
	{
		this.options = options;
		this.logger = logger;
		environment = services.GetService<IHostEnvironment>();

		catalog = new Lazy<MessageCatalog>(() => MessageCatalog.Build(options));
	}

	string Title => options.Title ?? environment?.ApplicationName ?? "TransitStop";

	public void LogMapped(string pattern) =>
		logger.LogInformation("TransitStop is available at {Path}", pattern);

	public async Task ListMessages(HttpContext context)
	{
		MessageCatalog messages = catalog.Value;

		object response = new
		{
			title = Title,
			messages = messages.Messages.Select(m => new
			{
				id = m.Id,
				name = m.Name,
				@namespace = m.Namespace,
				kind = m.Kind,
				sample = m.Sample,
			}),
		};

		context.Response.Headers.CacheControl = "no-store";
		await context.Response.WriteAsJsonAsync(response, ApiJson, context.RequestAborted);
	}
}
