# TransitStop

A development-time web UI for [MassTransit](https://masstransit.io): lists the message types your app knows about and lets you publish or send them from the browser.

## Usage

```csharp
builder.Services.AddMassTransit(x => { /* as usual */ });
builder.Services.AddTransitStop();

var app = builder.Build();
app.MapTransitStop();   // → /transit-stop/
```

Worker services without HTTP can use `AddTransitStop(o => o.UseStandaloneServer())` instead, which serves the UI on http://localhost:5380/.

## Demo

```bash
dotnet run --project TransitStop.Demo
```
