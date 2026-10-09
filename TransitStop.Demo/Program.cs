using MassTransit;
using TransitStop.Demo;
using TransitStop.Demo.Contracts;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
	x.AddConsumer<SubmitOrderConsumer>();

	// in-memory: nothing outside this process can publish here, which is what TransitStop is for
	x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
});

builder.Services.AddTransitStop(o => o
	.AddMessage<SubmitOrder>()
	.AddMessage<OrderSubmitted>());

WebApplication app = builder.Build();

app.MapTransitStop();
app.MapGet("/", () => Results.Redirect("/transit-stop/"));

app.Run();
