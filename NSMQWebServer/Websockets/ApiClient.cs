using BlazorSessionScopedContainer.Core;
using NSMQWebServer.Services;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using NSQM.Data.Model.Persistence;
using NSQM.Data.Model.Response;
using NSQM.Data.Networking;
using System;
using System.Net.WebSockets;
using System.Text;

namespace NSMQWebServer.Websockets
{
	public class ApiClient : NSQMWebSocket
	{
		private NSession? _session;
		private ChannelServices _channelServices;

		public ApiClient(WebSocket websocket, NSession? session) : base(websocket)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));
			_session = session;
			_channelServices = _session.GetGlobalService<ChannelServices>();
		}

		protected override async Task HandleMessage(NSQMessage message)
		{
			switch (message.Type)
			{
				case MessageType.Subscribe:
					await SubscribeUser(message, message.StructBuffer.ToStruct<NSQMSubscribeMessage>(Encoding.UTF8));
					break;
				case MessageType.Task:
					await CreateTask(message, message.StructBuffer.ToStruct<NSQMTaskMessage>(Encoding.UTF8));
					break;
				case MessageType.TaskStream:
					await StreamTask(message, message.StructBuffer.ToStruct<NSQMTaskMessage>(Encoding.UTF8));
					break;
				case MessageType.Ack:
					await Acknowledge(message, message.StructBuffer.ToStruct<NSQMAck>(Encoding.UTF8));
					break;
			}
		}

		private async Task Acknowledge(NSQMessage message, NSQMAck ackMessage)
		{
			if (ackMessage.AckType == AckType.Rejected)
			{
				var removeFromUserResponse = (await _channelServices.RemoveTaskFromUser(ackMessage.ToId, ackMessage.TaskId)).ConvertTo(model => (object)model);
				if (await Eval(message, removeFromUserResponse))
					return;
				await Reply<object>(message, removeFromUserResponse);
			}
			else if (ackMessage.AckType == AckType.Accepted)
			{
				var removeResponse = (await _channelServices.RemoveTask(ackMessage.TaskId)).ConvertTo(model => (object)model);
				if (await Eval(message, removeResponse))
					return;
				await Reply<object>(message, removeResponse);
			}
		}

		private async Task CreateTask(NSQMessage message, NSQMTaskMessage taskMessage)
		{
			var createResponse = await _channelServices.CreateTask(taskMessage.Map<NSQMTaskMessage, TaskData>());
			await Reply<TaskData>(message, createResponse);
		}

		private async Task StreamTask(NSQMessage message, NSQMTaskMessage taskMessage)
		{
			await _channelServices.StreamTask(taskMessage.Map<NSQMTaskMessage, TaskData>());
			// await Reply<TaskData>(message, streamResponse); not recommended, since unnecessary overhead
		}

		private async Task SubscribeUser(NSQMessage message, NSQMSubscribeMessage subscribeMessage)
		{
			var createResponse = await _channelServices.CreateUser(this, message.SenderId, subscribeMessage.UserType);
			if (await Eval(message, createResponse))
				return;

			var subscribeResponse = await _channelServices.AddUserToChannel(subscribeMessage.ChannelId, message.SenderId);
			if (await Eval(message, subscribeResponse))
				return;

			await Reply<User>(message, subscribeResponse);
		}

		private async Task Reply<T>(NSQMessage message, ApiResponseL1<T> response) where T : class
		{
			var httpResponse = new ApiResponseL2<T>(response);
			var nsqmInfoMessage = NSQMInfoMessage.Build(message.SenderId, httpResponse.ApiResponseLayer3, message.ConnectionId, Encoding.UTF8);
			await Send(nsqmInfoMessage);
		}

		private async Task<bool> Eval<T>(NSQMessage message, ApiResponseL1<T> response) where T : class
		{
			if (response.Model == null)
			{
				await Reply<T>(message, response);
				return true;
			}
			switch (response.Status)
			{
				case ApiStatusCode.Warning:
				case ApiStatusCode.Ok:
					{
						return false;
					}
			}
			await Reply<T>(message, response);
			return true;
		}
	}
}
