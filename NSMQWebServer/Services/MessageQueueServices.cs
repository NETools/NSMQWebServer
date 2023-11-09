using NSMQWebServer.Components;
using NSMQWebServer.Models;
using NSMQWebServer.Websockets;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using System.Net;
using System.Text;

namespace NSMQWebServer.Services
{
	public class MessageQueueServices
	{
		private SemaphoreSlim _serviceSemaphore = new SemaphoreSlim(1, 1);

		public Dictionary<string, Components.ChannelServices> Channels { get; private set; } = new Dictionary<string, Components.ChannelServices>();

		public async Task<ChannelServiceResult> CreateChannel(
			string channelName)
		{
			try
			{
				await _serviceSemaphore.WaitAsync();
				if (!Channels.ContainsKey(channelName))
				{
					Channels.Add(channelName, new Components.ChannelServices(channelName));

					return new ChannelServiceResult()
					{
						StatusCode = HttpStatusCode.Created,
						Message = "Channel has been created."
					};
				}

				return new ChannelServiceResult()
				{
					StatusCode = HttpStatusCode.BadRequest,
					Message = "Channel already exists"
				};
			}
			finally
			{
				_serviceSemaphore.Release();
			}
		}
		public async Task<ChannelServiceResult> BroadcastTask(
			string channelName, 
			string publisherId, 
			string taskName, 
			byte[] content)
		{
			try
			{
				await _serviceSemaphore.WaitAsync();

				if (Channels.ContainsKey(channelName))
				{
					var result = await Channels[channelName].BroadcastTask(publisherId, taskName, content);

					if (result == ChannelStatus.PublisherDoesntExist)
					{
						return new ChannelServiceResult()
						{
							StatusCode = HttpStatusCode.NotFound,
							Message = "Publisher not found."
						};
					}

					return new ChannelServiceResult()
					{
						StatusCode = HttpStatusCode.OK,
						Message = "Task has been sent"
					};
				}

				return new ChannelServiceResult()
				{
					StatusCode = HttpStatusCode.NotFound,
					Message = "Channel has not been found"
				};
			}
			finally
			{

				_serviceSemaphore.Release();
			}
		}

		public async Task ConsumerSubscribe(ApiClient client, NSQMConsumerSubscribeMessage message)
		{
			try
			{
				await _serviceSemaphore.WaitAsync();

				if (Channels.ContainsKey(message.ChannelName))
				{
					await Channels[message.ChannelName].ConsumerSubscribe(client, message.ConsumerName, message.ConsumerPriority);
				}
				else
				{
					await client.Send(NSQMInfoMessage.Build(InfoType.ChannelNotFound, 
						new
						{
							ApiResult = "CHANNEL_NOT_FOUND",
							ChannelName = message.ChannelName,
							Message = "Channel could not be found"
						}.ToJsonBytes(Encoding.UTF8), Encoding.UTF8));
				}
			}
			finally
			{
				_serviceSemaphore.Release();
			}
		}

		public async Task PublisherSubscribe(ApiClient client, NSQMPublisherSubscribeMessage message)
		{
			try
			{
				await _serviceSemaphore.WaitAsync();

				if (Channels.ContainsKey(message.ChannelName))
				{
					await Channels[message.ChannelName].ProducerSubscribe(client, message.PublisherId);
				}
				else
				{
					await client.Send(NSQMInfoMessage.Build(InfoType.ChannelNotFound,
							new
							{
								ApiResult = "CHANNEL_NOT_FOUND",
								ChannelName = message.ChannelName,
								Message = "Channel could not be found"
							}.ToJsonBytes(Encoding.UTF8), Encoding.UTF8));
				}
			}
			finally
			{
				_serviceSemaphore.Release();
			}
		}
		public async Task NotifyTaskResult(ApiClient client, NSQMTaskResult message)
		{
			try
			{
				await _serviceSemaphore.WaitAsync();

				if (Channels.ContainsKey(message.ChannelName))
				{
					await Channels[message.ChannelName].NotifyTaskResult(message.Id, message.ConsumerName, message.Content);
				}
				else
				{
					await client.Send(NSQMInfoMessage.Build(InfoType.ChannelNotFound,
							new
							{
								ApiResult = "CHANNEL_NOT_FOUND",
								ChannelName = message.ChannelName,
								Message = "Channel could not be found"
							}.ToJsonBytes(Encoding.UTF8), Encoding.UTF8));
				}
			}
			finally
			{
				_serviceSemaphore.Release();
			}
		}
	}
}