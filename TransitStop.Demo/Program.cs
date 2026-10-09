using MassTransit;
using TransitStop.Demo;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
	// NotifyCustomerConsumer listens on "notify-customer" - the queue to Send to
	x.SetKebabCaseEndpointNameFormatter();

	x.AddConsumer<SubmitOrderConsumer>();
	x.AddConsumer<NotifyCustomerConsumer>();
	x.AddSagaStateMachine<OrderStateMachine, OrderState>().InMemoryRepository();

	// in-memory: nothing outside this process can publish here, which is what TransitStop is for
	x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

// everything consumed above is listed automatically
builder.Services.AddTransitStop();

WebApplication app = builder.Build();

app.MapTransitStop();
app.MapGet("/", () => Results.Redirect("/transit-stop/"));

app.Run();
