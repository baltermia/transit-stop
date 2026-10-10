using MassTransit;
using TransitStop.Demo;
using TransitStop.Demo.Contracts;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
	// NotifyCustomerConsumer listens on "notify-customer" - the queue to Send to
	x.SetKebabCaseEndpointNameFormatter();

	x.AddConsumer<SubmitOrderConsumer>();
	x.AddConsumer<NotifyCustomerConsumer>();
	x.AddConsumer<SensorReadingsConsumer>();
	x.AddSagaStateMachine<OrderStateMachine, OrderState>().InMemoryRepository();

	// in-memory: nothing outside this process can publish here, which is what TransitStop is for
	x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

// everything consumed above is listed automatically. CancelOrder is only consumed by "another
// service", so the contracts namespace is added explicitly - minus the OrderLine DTO
builder.Services.AddTransitStop(o => o
	.AddMessagesFromNamespaceContaining<SubmitOrder>()
	.Exclude(t => t == typeof(OrderLine)));

WebApplication app = builder.Build();

app.MapTransitStop();
app.MapGet("/", () => Results.Redirect("/transit-stop/"));

app.Run();
