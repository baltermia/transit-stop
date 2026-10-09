using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MassTransit;
using MassTransit.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TransitStop.Internal;

/// <summary>
/// The request handlers behind the UI. Lives in the app's container, so it uses the app's bus.
/// </summary>
internal sealed class TransitStopRuntime
{
	static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	readonly IServiceProvider services;
	readonly TransitStopOptions options;
	readonly IHostEnvironment? environment;
	readonly ILogger logger;
	readonly Lazy<MessageCatalog> catalog;
	readonly Lazy<Func<string, Type, object?>> deserializer;
	readonly Lazy<string> page;

	public TransitStopRuntime(IServiceProvider services, TransitStopOptions options, RegisteredServices registered, ILogger<TransitStopRuntime> logger)
	{
		this.services = services;
		this.options = options;
		this.logger = logger;
		environment = services.GetService<IHostEnvironment>();

		catalog = new Lazy<MessageCatalog>(() => MessageCatalog.Build(options, registered));
		deserializer = new Lazy<Func<string, Type, object?>>(() => options.Deserializer ?? DefaultDeserializer());
		page = new Lazy<string>(LoadPage);
	}

	string Title => options.Title ?? environment?.ApplicationName ?? "TransitStop";

	public void LogMapped(string pattern) =>
		logger.LogInformation("TransitStop is available at {Path}", pattern);

	public async Task ServePage(HttpContext context)
	{
		context.Response.ContentType = "text/html; charset=utf-8";
		await context.Response.WriteAsync(page.Value, context.RequestAborted);
	}

	public async Task ListMessages(HttpContext context)
	{
		MessageCatalog messages = catalog.Value;

		object response = new
		{
			title = Title,
			bus = services.GetService<IBus>()?.Address.ToString(),
			messages = messages.Messages.Select(m => new
			{
				id = m.Id,
				name = m.Name,
				@namespace = m.Namespace,
				kind = m.Kind,
				consumers = m.Consumers,
				sample = m.Sample,
			}),
		};

		context.Response.Headers.CacheControl = "no-store";
		await context.Response.WriteAsJsonAsync(response, ApiJson, context.RequestAborted);
	}

	public async Task Publish(HttpContext context)
	{
		JsonDocument document;
		try
		{
			document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
		}
		catch (JsonException e)
		{
			await Error(context, HttpStatusCode.BadRequest, $"The request is not valid JSON: {e.Message}");
			return;
		}

		using (document)
		{
			JsonElement root = document.RootElement;

			string? id = root.TryGetProperty("messageType", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
				? idElement.GetString()
				: null;

			if (id is null || !catalog.Value.TryGet(id, out MessageInfo info))
			{
				await Error(context, HttpStatusCode.NotFound, $"Unknown message type '{id}'.");
				return;
			}

			if (!root.TryGetProperty("message", out JsonElement messageElement))
			{
				await Error(context, HttpStatusCode.BadRequest, "The request has no 'message'.");
				return;
			}

			object message;
			try
			{
				message = deserializer.Value(messageElement.GetRawText(), info.Type)!;
			}
			catch (Exception e)
			{
				await Error(context, HttpStatusCode.BadRequest, $"Could not deserialize the JSON into {info.Name}: {e.Message}");
				return;
			}

			IBus? bus = services.GetService<IBus>();
			if (bus is null)
			{
				await Error(context, HttpStatusCode.InternalServerError, "No MassTransit bus (IBus) is registered in this app.");
				return;
			}

			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				await bus.Publish(message, info.Type, context.RequestAborted);
			}
			catch (Exception e)
			{
				logger.LogError(e, "TransitStop failed to publish {MessageType}", info.Name);
				await Error(context, HttpStatusCode.InternalServerError, $"Publish failed: {e.Message}");
				return;
			}

			logger.LogInformation("TransitStop published {MessageType}", info.Name);

			await context.Response.WriteAsJsonAsync(new
			{
				ok = true,
				messageType = info.Id,
				elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
			}, ApiJson, context.RequestAborted);
		}
	}

	static Task Error(HttpContext context, HttpStatusCode status, string error)
	{
		context.Response.StatusCode = (int)status;
		return context.Response.WriteAsJsonAsync(new { ok = false, error }, ApiJson, context.RequestAborted);
	}

	/// <summary>MassTransit's own System.Text.Json settings, which also handle interface messages.</summary>
	static Func<string, Type, object?> DefaultDeserializer() =>
		(text, type) => JsonSerializer.Deserialize(text, type, SystemTextJsonMessageSerializer.Options);

	string LoadPage()
	{
		using Stream stream = typeof(TransitStopRuntime).Assembly.GetManifestResourceStream("TransitStop.ui.index.html")
			?? throw new InvalidOperationException("The TransitStop UI resource is missing.");
		using StreamReader reader = new(stream);

		return reader.ReadToEnd().Replace("{{TITLE}}", WebUtility.HtmlEncode(Title), StringComparison.Ordinal);
	}
}
