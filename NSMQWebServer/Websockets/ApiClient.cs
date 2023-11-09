using NSMQWebServer.Services;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using NSQM.Data.Networking;
using System;
using System.Net.WebSockets;
using System.Text;

namespace NSMQWebServer.Websockets
{
	public class ApiClient : NSQMWebSocket
	{
		private MessageQueueServices _channelServices;
	
		public ApiClient(WebSocket websocket, MessageQueueServices channelServices) : base(websocket) 
		{
			_channelServices = channelServices;
		}

		protected override async Task HandleMessage(NSQMessage message)
		{
			switch (message.Type)
			{
				case MessageType.ConsumerSubscribe:
					await _channelServices.ConsumerSubscribe(
						this,
						message.StructBuffer.ToStruct<NSQMConsumerSubscribeMessage>(Encoding.UTF8));
					break;
				case MessageType.PublisherSubscribe:
					await _channelServices.PublisherSubscribe(
						this,
						message.StructBuffer.ToStruct<NSQMPublisherSubscribeMessage>(Encoding.UTF8));
					break;
				case MessageType.RunTaskEnd:
					await _channelServices.NotifyTaskResult(
						this, 
						message.StructBuffer.ToStruct<NSQMTaskResult>(Encoding.UTF8));
					break;
			}
		}

	}
}
