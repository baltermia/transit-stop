# TransitStop

A development-time web UI for [MassTransit](https://masstransit.io). Attach it to any app and you get a page that:

- **lists every message type** the app knows about. Types consumed by its consumers, job consumers and sagas are found automatically, and you can add more yourself.
- **lets you search** them by name, CamelCase initials (`SO` → `SubmitOrder`), namespace or consumer.
- **pre-fills an editable JSON payload** generated from the type's properties. Enum values are listed alongside.
- **publishes** the message, or **sends** it to a queue/exchange, through the app's own bus, with optional headers.
- keeps your **edited drafts** and a **recent** list per browser, so you can replay a payload with one click.

It dispatches through the app's `IBus`, so it works with **any transport**: RabbitMQ, Azure Service Bus, Amazon SQS, and the **in-memory** transport too. In-memory is the case where nothing outside the process can reach the bus.

TransitStop is **only active in the `Development` environment** by default.

---

## Getting started

### 1. Reference the package

From a local build:

```xml
<ProjectReference Include="..\..\transit-stop\TransitStop\TransitStop.csproj" />
```

or pack it to a local feed and reference the package:

```bash
dotnet pack TransitStop -c Release -o C:/nuget-local
```

```xml
<PackageReference Include="TransitStop" Version="0.1.0" />
```

> **Tip – zero production footprint:** reference it only in Debug builds and wrap the calls in `#if DEBUG`:
>
> ```xml
> <PackageReference Include="TransitStop" Version="0.1.0" Condition="'$(Configuration)' == 'Debug'" />
> ```

### 2a. Web apps (ASP.NET Core)

```csharp
builder.Services.AddMassTransit(x => { /* as usual */ });
builder.Services.AddTransitStop();

var app = builder.Build();
app.MapTransitStop();          // → http://localhost:<port>/transit-stop/
app.Run();
```

`MapTransitStop("/some/other/path")` changes the route. It returns an endpoint convention builder, so `.RequireAuthorization()` and similar conventions work too.

### 2b. Worker services and console apps (no HTTP pipeline)

TransitStop can bring its own small web server:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMassTransit(x => { /* as usual */ });
builder.Services.AddTransitStop(o => o.UseStandaloneServer(port: 5380));

builder.Build().Run();         // → http://localhost:5380/
```

The standalone server:
- listens on **localhost only**.
- is built from an empty builder, so the app's URLs, Kestrel settings and middleware don't affect it.
- logs an error and lets the app run on without TransitStop if the port is taken.

---

## Which messages are listed

| Source | How |
|---|---|
| Consumed by this app | Automatic (`DiscoverConsumedMessages = true`). TransitStop reads the app's service registrations and collects the message types of `IConsumer<T>`, `IConsumer<Batch<T>>`, `IJobConsumer<T>`, `InitiatedBy<T>` / `Orchestrates<T>` / `Observes<T, _>`, and every `Event<T>` property of a `MassTransitStateMachine`. The UI shows which consumer handles each message. |
| A single type | `o.AddMessage<OrderSubmitted>()` |
| A contracts namespace | `o.AddMessagesFromNamespaceContaining<OrderSubmitted>()`. Use this for messages *other* services consume. |
| A whole assembly | `o.AddMessagesFromAssemblyContaining<OrderSubmitted>(t => t.Name.EndsWith("Command"))` |
| Hide some | `o.Exclude(t => t.Namespace!.EndsWith(".Internal"))` |

Assembly and namespace scans pick up public, non-abstract classes, records and interfaces. They skip consumers, sagas, state machines, exceptions, attributes and anything derived from a MassTransit class. Nested DTOs that live next to your messages, such as an `Address` record, are listed too. Hide them with `Exclude` if they get in the way.

```csharp
builder.Services.AddTransitStop(o => o
    .AddMessagesFromNamespaceContaining<OrderSubmitted>()   // other services' messages
    .Exclude(t => t.Name.EndsWith("Dto")));
```

---

## Serialization

TransitStop turns your edited JSON into the message object, then hands that object to the bus. The bus serializes it as usual. By default the JSON is read with **MassTransit's own System.Text.Json settings**, plus enums written as names. Interface messages work out of the box.

If your bus uses **Newtonsoft.Json** with custom settings or converters, deserialize with those same settings:

```csharp
builder.Services.AddTransitStop(o =>
{
    JsonSerializerSettings settings = MyDefaults.MassTransitJsonSettings;
    o.Deserializer = (json, type) => JsonConvert.DeserializeObject(json, type, settings);
});
```

> Newtonsoft cannot create **interface** messages from JSON. If you have interface contracts, keep the default deserializer, or handle `type.IsInterface` separately.

Sample payloads use camelCase by default and honour `[JsonPropertyName]`, `[JsonProperty]` and `[JsonIgnore]`. To change the naming, set `o.SampleNamingPolicy` (`null` keeps C# names).

---

## Using the UI

| | |
|---|---|
| `/` or `Ctrl+K` | Focus search |
| `↑` `↓` `Enter` | Pick a message from the search results and jump to the editor |
| `Ctrl+Enter` | Publish / Send |
| `Alt+Shift+F` | Format the JSON |
| `Tab` | Indent in the editor |

- **Publish** goes to every subscriber of the message type.
- **Send** goes to one destination. It accepts:
  - a bare queue name (`order-service`, which means `queue:order-service`)
  - `queue:name` or `exchange:name`
  - a full transport address (`rabbitmq://localhost/vhost/order-service`)
- **Headers** is an optional JSON object, e.g. `{"tenant": "acme"}`, added to every dispatch.
- **Drafts**: once you edit a payload it's remembered per message type (marked *edited*). **Reset to sample** brings back the generated one.
- **Recent**: click an entry to load that payload (and mode/destination) back into the editor.
- Drafts, recent dispatches and settings live in your **browser's localStorage**, separately for each app. Nothing is stored server-side.
- Each dispatch is also logged by the app (`TransitStop published SubmitOrder`).

---

## Options

```csharp
builder.Services.AddTransitStop(o =>
{
    o.Title = "Order service";                                // default: application name
    o.EnableWhen = env => env.IsDevelopment() || env.IsEnvironment("Local");
    o.DiscoverConsumedMessages = true;
    o.AddMessagesFromNamespaceContaining<OrderSubmitted>();
    o.Exclude(t => t.Name.EndsWith("Dto"));
    o.Deserializer = (json, type) => /* ... */;
    o.SampleNamingPolicy = JsonNamingPolicy.CamelCase;
    o.UseStandaloneServer(5380);                              // hosts without HTTP
});
```

| Option | Default | |
|---|---|---|
| `EnableWhen` | `env.IsDevelopment()` | When false, `MapTransitStop` maps nothing (404) and the standalone server does not start. |
| `DiscoverConsumedMessages` | `true` | List the messages this app's consumers and sagas consume. |
| `Deserializer` | MassTransit STJ + string enums | `(json, type) => object` |
| `SampleNamingPolicy` | camelCase | Property naming in generated samples. |
| `Title` | app name | Shown in the header and browser tab. |
| `UseStandaloneServer(port)` | off (port `5380` when called without arguments) | Own Kestrel server on `localhost:port`. |
