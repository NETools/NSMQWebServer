using NSMQWebServer.Model;
using NSMQWebServer.Model.Contracts;
using NSMQWebServer.Persistence;
using NSMQWebServer.Websockets;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using NSQM.Data.Model.Persistence;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Channel = NSQM.Data.Model.Persistence.Channel;

namespace NSMQWebServer.Services
{
	public class ConnectivityServices : INSQMDbContext<MessageQueueDbContext>
	{
		private List<ConnectedUser> _connectedUsers;

		public MessageQueueDbContext Context { get; set; }

		public ConnectivityServices()
		{
			_connectedUsers = new List<ConnectedUser>();
		}

		public async Task Send(Guid senderId, Guid receiverId, TaskData message, bool isStreamed)
		{
			var taskMessage = NSQMTaskMessage.Build(
					senderId,
					message.FromId,
					message.ToId,
					message.TaskName,
					message.TaskId,
					message.ChannelId,
					message.Status,
					message.Content,
					message.AddresseeType,
					message.SenderType, Encoding.UTF8, isStreamed);

			if (receiverId == Guid.Empty) // is interpreted as broadcast
			{
				for (int i = 0; i < _connectedUsers.Count; i++)
				{
					var p = _connectedUsers[i];
					if (p.User.UserType != message.AddresseeType || !p.ChannelIds.Contains(message.ChannelId))
						continue;
					await p.Connection.Send(taskMessage);
				}

				return;
			}

			var receiverClient = _connectedUsers.Find(p => p.User.UserId == receiverId);
			if (receiverClient == null)
			{
				return;
			}

			await receiverClient.Connection.Send(taskMessage);
		}

		//public async Task Send(Guid senderId, Guid receiverId, NSQMAck message)
		//{
		//	var ackMessage = NSQMAck.Build(senderId, message.FromId, message.ToId, message.TaskId, message.ChannelId, message.AckType, message.UserType);
		//	var receiverClient = _connectedUsers.Find(p => p.User.UserId == receiverId);
		//	if (receiverClient == null)
		//	{
		//		return;
		//	}

		//	await receiverClient.Connection.Send(ackMessage);
		//}

		public bool RegisterConnection(ApiClient client, User? user)
		{
			if (_connectedUsers.Any(p => p.User.UserId == user.UserId))
				return false;

			var connectedUser = new ConnectedUser()
			{
				Connection = client,
				User = user
			};

			_connectedUsers.Add(connectedUser);
			connectedUser.Connection.Disconnected += UserDisconnected;
			return true;
		}

		public void RegisterUserChannel(User user, string channelId)
		{
			if (!UserConnected(user))
				return;
			_connectedUsers.Find(p => p.User.UserId == user.UserId)!.ChannelIds.Add(channelId);
		}

		public bool UserConnected(User user)
		{
			return _connectedUsers.Any(p => p.User.UserId == user.UserId);
		}

		private async void UserDisconnected(Guid clientId)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Client with the id {clientId} has disconnected.");
			Console.ForegroundColor = ConsoleColor.Gray;


			var connectedUser = _connectedUsers.Find(p => p.Connection.Id == clientId);
			if (connectedUser == null) return;
			var userId = connectedUser.User.UserId;
			await Context.RemoveUser(userId);
			_connectedUsers.RemoveAll(p => p.User.UserId == userId);
		}
	}
}
