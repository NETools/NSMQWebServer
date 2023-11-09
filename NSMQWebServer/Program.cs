using NSMQWebServer.Services;
using NSMQWebServer.Websockets;

var builder = WebApplication.CreateBuilder(args);
var messageQueueServices = new MessageQueueServices();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(messageQueueServices);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

var webSocketOptions = new WebSocketOptions
{
	KeepAliveInterval = TimeSpan.FromMinutes(2)
};
app.UseWebSockets(webSocketOptions);

app.Use(async (context, next) =>
{
	if (context.Request.Path == "/")
	{
		if (context.WebSockets.IsWebSocketRequest)
		{
			using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
			var listener = new ApiClient(webSocket, messageQueueServices);
			await listener.Start();
		}
		else
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
		}
	}
	else
	{
		await next(context);
	}
});


app.UseAuthorization();

app.MapControllers();

app.Run();
