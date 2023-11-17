using BlazorSessionScopedContainer.Core;
using NSMQWebServer.Persistence;
using NSMQWebServer.Services;
using NSMQWebServer.Websockets;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<NSession>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


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
	var scope = app.Services.CreateAsyncScope();
	var session = scope.ServiceProvider.GetService<NSession>();
	InitSession(session);

	if (context.Request.Path == "/")
	{
		if (context.WebSockets.IsWebSocketRequest)
		{
			var webSocket = await context.WebSockets.AcceptWebSocketAsync();
			var listener = new ApiClient(webSocket, session);
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


void InitSession(NSession? session)
{
	if(session == null)
		throw new ArgumentNullException(nameof(session));

	session.StartSession((id, handler) =>
	{
		handler.SetCurrentMigrationContext(new BasicMigrationContext());

		handler.AddGlobalService<ConnectivityServices>();
		handler.AddGlobalService<ChannelServices>();
	});

}