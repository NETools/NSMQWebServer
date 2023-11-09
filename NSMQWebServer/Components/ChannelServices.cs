using NSMQWebServer.Models;
using NSMQWebServer.Models.Structs;
using NSMQWebServer.Websockets;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using System.Text;
using System.Threading.Tasks;

namespace NSMQWebServer.Components
{
    public enum ChannelStatus
	{
		PublisherDoesntExist,
		Ok
	};

    public class ChannelServices
	{
		private static int _TaskCounter = 0;

		public string ChannelName { get; private set; }

		public List<MQConsumerInfo> Consumers { get; private set; } = new List<MQConsumerInfo>();
		public List<MQPublisherInfo> Publishers { get; private set; } = new List<MQPublisherInfo>();

		public List<EnqueuedTask> EnqueuedTasks { get; private set; } = new List<EnqueuedTask>();
		public List<EnqueuedTask> EnqueuedTaskResults { get; private set; } = new List<EnqueuedTask>();

		public ChannelServices(string channelName)
		{
			ChannelName = channelName;
		}

		public async Task<ChannelStatus> BroadcastTask(string publisherId, string taskName, byte[] content)
		{
			if(!Publishers.Exists(p=>p.PublisherId == publisherId))
			{
				return ChannelStatus.PublisherDoesntExist;
			}

			var taskMessage = new NSQMTaskMessage()
			{
				ChannelName = ChannelName,
				Content = content,
				Id = Guid.NewGuid(),
				Name = taskName
			};

			var message = new NSQMessage()
			{
				Type = MessageType.RunTaskBegin,
				StructBuffer = taskMessage.ToJsonBytes(Encoding.UTF8)
			}; 

			foreach (var consumer in Consumers)
			{
				await consumer.Client.Send(message);
			}

			var enqueuedTask = new EnqueuedTask()
			{
				Index = _TaskCounter++,
				PublisherId = publisherId,
				Task = taskMessage
			};

			EnqueuedTasks.Add(enqueuedTask);

			return ChannelStatus.Ok;
		}
		
		public async Task ConsumerSubscribe(ApiClient client, string consumerName, int consumerPriority)
		{
			if (Consumers.Exists(p => p.ConsumerName == consumerName))
			{
				await client.Send(NSQMInfoMessage.Build(InfoType.ConsumerAlreadyExists,
						new
						{
							ApiResult = "CONSUMER_EXISTS_IN_CHANNEL",
							ChannelName = ChannelName,
							Message = $"Consumer with name {consumerName} has already subscribed to channel {ChannelName}"
						}.ToJsonBytes(Encoding.UTF8), 
						Encoding.UTF8));
				return;
			}

			var consumerInfo = new MQConsumerInfo()
			{
				Client = client,
				ConsumerName = consumerName,
				ConsumerPriority = consumerPriority
			};

			Consumers.Add(consumerInfo);

			await client.Send(
				NSQMInfoMessage.Build(
					InfoType.Subscribed,
						new
						{
							ApiResult = "CONSUMER_SUBSCRIBED",
							ChannelName = ChannelName,
							Message = $"Consumer with name {consumerName} has been subscribed to channel {ChannelName}"
						}.ToJsonBytes(Encoding.UTF8),
					Encoding.UTF8));

			client.Disconnected += OnConsumerDisconnected;

			await ProcessQueue();
		}
		public async Task ProducerSubscribe(ApiClient client, string publisherId)
		{
			if (Publishers.Exists(p => p.PublisherId == publisherId))
			{
				await client.Send(NSQMInfoMessage.Build(InfoType.PublisherAlreadyExists,
					new
					{
						ApiResult = "PUBLISHER_EXISTS_IN_CHANNEL",
						ChannelName = ChannelName,
						Message = $"Producer with id {publisherId} is already subscribed to channel {ChannelName}"
					}.ToJsonBytes(Encoding.UTF8), 
					Encoding.UTF8));

				return;
			}

			if(EnqueuedTaskResults.Exists(p=>p.PublisherId == publisherId))
			{
				await ProcessEnqueuedResults(client, publisherId);
			}

			var publisherInfo = new MQPublisherInfo()
			{
				Client = client,
				PublisherId = publisherId
			};

			Publishers.Add(publisherInfo);

			await client.Send(
				NSQMInfoMessage.Build(
					InfoType.Subscribed,
					new
					{
						ApiResult = "PUBLISHER_SUBSCRIBED",
						ChannelName = ChannelName,
						Message = $"Publisher with id {publisherId} has been subscribed to channel {ChannelName}"
					}.ToJsonBytes(Encoding.UTF8),
					Encoding.UTF8));

			client.Disconnected += OnPublisherDisconnected;
		}
		public async Task NotifyTaskResult(Guid taskId, string consumerName, byte[] content)
		{
			if (EnqueuedTasks.Exists(p=>p.Task.Id == taskId))
			{
				var enqueuedTask = EnqueuedTasks.FirstOrDefault(p => p.Task.Id == taskId);
				var publisher = Publishers.FirstOrDefault(p => p.PublisherId == enqueuedTask.PublisherId);

				if (publisher != null)
				{
					await publisher.Client.Send(
						NSQMTaskResult.Build(taskId, ChannelName, consumerName, TaskResultType.TaskDone, content, Encoding.UTF8));

					EnqueuedTasks.Remove(enqueuedTask);
				}
				else if (!EnqueuedTaskResults.Exists(p => p.Task.Id == taskId))
				{
					EnqueuedTaskResults.Add(new EnqueuedTask()
					{
						Index = enqueuedTask.Index,
						PublisherId = enqueuedTask.PublisherId,
						Task = new NSQMTaskMessage()
						{
							ChannelName = ChannelName,
							Content = content,
							Id = taskId
						}
					});
				}
			}
		}

		private void OnConsumerDisconnected(Guid clientId)
		{
			Consumers.RemoveAll(p => p.Client.Id == clientId);
		}
		private void OnPublisherDisconnected(Guid clientId)
		{
			Publishers.RemoveAll(p => p.Client.Id == clientId);
		}

		private async Task ProcessQueue()
		{
			var orderedTasks = EnqueuedTasks.OrderBy(p => p.Index);
			foreach (var task in orderedTasks)
			{
				var message = new NSQMessage()
				{
					Type = MessageType.RunTaskBegin,
					StructBuffer = task.Task.ToJsonBytes(Encoding.UTF8)
				};

				foreach (var consumer in Consumers)
				{
					await consumer.Client.Send(message);
				}
			}
		}
		private async Task ProcessEnqueuedResults(ApiClient publisherClient, string publisherId)
		{
			var orderedTasks = EnqueuedTaskResults.Where(p => p.PublisherId == publisherId).OrderBy(p => p.Index);
			foreach (var task in orderedTasks)
			{
				var message = new NSQMessage()
				{
					Type = MessageType.RunTaskBegin,
					StructBuffer = task.Task.ToJsonBytes(Encoding.UTF8)
				};

				await publisherClient.Send(
					NSQMTaskResult.Build(task.Task.Id, ChannelName, "", TaskResultType.TaskDone, task.Task.Content, Encoding.UTF8));

				EnqueuedTasks.RemoveAll(p => p.Task.Id == task.Task.Id);
			}

			EnqueuedTaskResults.RemoveAll(p => p.PublisherId == publisherId);
		}
	}
}
