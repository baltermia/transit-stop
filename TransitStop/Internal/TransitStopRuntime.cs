using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MassTransit;
using MassTransit.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TransitStop.Internal;

/// <summary>
/// The request handlers behind the UI. Lives in the app's container, so it uses the app's bus
/// whether it is served from the app's own pipeline or from the standalone server.
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

	/// <summary>Without a host there is no environment to protect, e.g. in tests.</summary>
	public bool IsEnabled => environment is null || options.EnableWhen(environment);

	string Title => options.Title ?? environment?.ApplicationName ?? "TransitStop";

	public void LogDisabled() =>
		logger.LogInformation("TransitStop is disabled in the {Environment} environment", environment?.EnvironmentName);

	public void LogMapped(string pattern) =>
		logger.LogInformation("TransitStop is available at {Path}", pattern);

	public async Task ServePage(HttpContext context)
	{
		// the page fetches its API relative to its own URL, which needs the trailing slash
		PathString path = context.Request.PathBase + context.Request.Path;
		if (!path.HasValue || !path.Value!.EndsWith('/'))
		{
			context.Response.Redirect(path + "/" + context.Request.QueryString);
			return;
		}

		context.Response.ContentType = "text/html; charset=utf-8";
		context.Response.Headers.CacheControl = "no-store";
		await context.Response.WriteAsync(page.Value, context.RequestAborted);
	}

	public async Task ListMessages(HttpContext context)
	{
		MessageCatalog messages = catalog.Value;

		object response = new
		{
			title = Title,
			environment = environment?.EnvironmentName,
			bus = services.GetService<IBus>()?.Address.ToString(),
			messages = messages.Messages.Select(m => new
			{
				id = m.Id,
				name = m.Name,
				@namespace = m.Namespace,
				kind = m.Kind,
				consumers = m.Consumers,
				sample = m.Sample,
				enums = m.Enums,
			}),
		};

		context.Response.Headers.CacheControl = "no-store";
		await context.Response.WriteAsJsonAsync(response, ApiJson, context.RequestAborted);
	}

	public async Task Dispatch(HttpContext context, bool send)
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

			Uri? destination = null;
			if (send)
			{
				string? raw = root.TryGetProperty("destination", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
				if (!TryParseDestination(raw, out destination))
				{
					await Error(context, HttpStatusCode.BadRequest,
						$"'{raw}' is not a valid destination. Use a queue name, or an address such as 'queue:my-queue' or 'exchange:my-exchange'.");
					return;
				}
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

			Dictionary<string, object?> headers = root.TryGetProperty("headers", out JsonElement h) ? ReadHeaders(h) : [];

			IBus? bus = services.GetService<IBus>();
			if (bus is null)
			{
				await Error(context, HttpStatusCode.InternalServerError, "No MassTransit bus (IBus) is registered in this app.");
				return;
			}

			Stopwatch stopwatch = Stopwatch.StartNew();
			try
			{
				if (destination is not null)
				{
					ISendEndpoint endpoint = await bus.GetSendEndpoint(destination);
					await endpoint.Send(message, info.Type, Pipe.Execute<SendContext>(c => SetHeaders(c, headers)), context.RequestAborted);
				}
				else
				{
					await bus.Publish(message, info.Type, Pipe.Execute<PublishContext>(c => SetHeaders(c, headers)), context.RequestAborted);
				}
			}
			catch (Exception e)
			{
				logger.LogError(e, "TransitStop failed to {Mode} {MessageType}", send ? "send" : "publish", info.Name);
				await Error(context, HttpStatusCode.InternalServerError, $"{(send ? "Send" : "Publish")} failed: {e.Message}");
				return;
			}

			if (send)
				logger.LogInformation("TransitStop sent {MessageType} to {Destination}", info.Name, destination);
			else
				logger.LogInformation("TransitStop published {MessageType}", info.Name);

			await context.Response.WriteAsJsonAsync(new
			{
				ok = true,
				mode = send ? "send" : "publish",
				messageType = info.Id,
				destination = destination?.ToString(),
				elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
			}, ApiJson, context.RequestAborted);
		}
	}

	static bool TryParseDestination(string? raw, out Uri? destination)
	{
		destination = null;
		if (string.IsNullOrWhiteSpace(raw))
			return false;

		raw = raw.Trim();

		// a bare name means a queue, the same short form MassTransit accepts
		string address = raw.Contains(':') ? raw : "queue:" + raw;
		return Uri.TryCreate(address, UriKind.Absolute, out destination);
	}

	static Dictionary<string, object?> ReadHeaders(JsonElement element)
	{
		Dictionary<string, object?> headers = new(StringComparer.Ordinal);
		if (element.ValueKind != JsonValueKind.Object)
			return headers;

		foreach (JsonProperty property in element.EnumerateObject())
		{
			headers[property.Name] = property.Value.ValueKind switch
			{
				JsonValueKind.String => property.Value.GetString(),
				JsonValueKind.Number => property.Value.TryGetInt64(out long l) ? l : property.Value.GetDouble(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Null => null,
				_ => property.Value.GetRawText(),
			};
		}

		return headers;
	}

	static void SetHeaders(SendContext context, Dictionary<string, object?> headers)
	{
		foreach ((string key, object? value) in headers)
			context.Headers.Set(key, value);
	}

	static Task Error(HttpContext context, HttpStatusCode status, string error)
	{
		context.Response.StatusCode = (int)status;
		return context.Response.WriteAsJsonAsync(new { ok = false, error }, ApiJson, context.RequestAborted);
	}

	/// <summary>
	/// MassTransit's own System.Text.Json settings (which also handle interface messages), plus
	/// enums as names so the generated samples are readable. Numbers are still accepted.
	/// </summary>
	static Func<string, Type, object?> DefaultDeserializer()
	{
		JsonSerializerOptions json = new(SystemTextJsonMessageSerializer.Options);
		json.Converters.Insert(0, new JsonStringEnumConverter());
		return (text, type) => JsonSerializer.Deserialize(text, type, json);
	}

	string LoadPage()
	{
		using Stream stream = typeof(TransitStopRuntime).Assembly.GetManifestResourceStream("TransitStop.ui.index.html")
			?? throw new InvalidOperationException("The TransitStop UI resource is missing.");
		using StreamReader reader = new(stream);

		return reader.ReadToEnd().Replace("{{TITLE}}", WebUtility.HtmlEncode(Title), StringComparison.Ordinal);
	}
}
