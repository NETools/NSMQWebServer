using BlazorSessionScopedContainer.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.HttpResults;
using NSMQWebServer.Model;
using NSMQWebServer.Model.Contracts;
using NSMQWebServer.Persistence;
using NSMQWebServer.Websockets;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using NSQM.Data.Model.Persistence;
using NSQM.Data.Model.Response;
using System.Threading.Channels;
using Channel = NSQM.Data.Model.Persistence.Channel;
using TaskStatus = NSQM.Data.Extensions.TaskStatus;

namespace NSMQWebServer.Services
{
	public class ChannelServices : INSQMDbContext<MessageQueueDbContext>
	{
		private ConnectivityServices _connectivityServices;
		public MessageQueueDbContext Context { get; set; }

		public ChannelServices( 
			ConnectivityServices connectivityServices)
		{
			_connectivityServices = connectivityServices;
		}

		public async Task<ApiResponseL1<User>> CreateUser(ApiClient client, Guid userId, UserType userType)
		{
			var response = await Context.CreateUser(userId, userType);
			_connectivityServices.RegisterConnection(client, response.Model);
			return response;
		}

		public async Task<ApiResponseL1<Channel>> CreateChannel(string channelId)
		{
			var channel = await Context.CreateChannel(channelId);
			return channel;
		}

		public async Task<ApiResponseL1<TaskData>> StreamTask(TaskData taskData)
		{
			await _connectivityServices.Send(taskData.FromId, taskData.ToId, taskData, true);
			return ApiResponseL1<TaskData>.Ok(taskData, "Task successfully streamed.", ApiResponseL1.TaskDataCreated);
		}

		public async Task<ApiResponseL1<TaskData>> CreateTask(TaskData taskData)
		{
			var response = await Context.CreateTaskData(taskData);
			if (response.Status != ApiStatusCode.Ok && response.Status != ApiStatusCode.Warning)
			{
				return response;
			}
			var result = (await AddTaskToUser(taskData)).ConvertTo<TaskData>(model => response.Model);

			return result;
		}

		public async Task<ApiResponseL1<User>> AddUserToChannel(string channelId, Guid userId)
		{
			var response = await Context.AddUserToChannel(channelId, userId);
			var user = response.Model;
			if (response.Status != ApiStatusCode.Ok && response.Status != ApiStatusCode.Warning)
				return response;

			await Context.ForAllOppositeUser(channelId, user!.UserType, async (channel, peer) =>
			{
				if (peer.TaskDatas == null)
					return;
				foreach (var task in peer.TaskDatas)
				{
					if (task.ToId != userId && task.ToId != Guid.Empty)
						continue;
					await _connectivityServices.Send(task.FromId, userId, task, false);
				}
			});

			_connectivityServices.RegisterUserChannel(user, channelId);

			return response;
		}

		public async Task<ApiResponseL1<User>> AddTaskToUser(TaskData taskData)
		{
			var response = await Context.AddTaskToUser(taskData.FromId, taskData.PhaseId);
			if (response.Status != ApiStatusCode.Ok && response.Status != ApiStatusCode.Warning)
				return response;
			await _connectivityServices.Send(taskData.FromId, taskData.ToId, taskData, false);

			return response;
		}

		public async Task<ApiResponseL1<User>> RemoveTaskFromUser(Guid userId, Guid taskId)
		{
			var user = await Context.RemoveTaskFromUser(userId, taskId);
			return user;
		}

		public async Task<ApiResponseL1<TaskData>> RemoveTask(Guid taskId)
		{
			var taskData = await Context.RemoveTaskData(taskId);
			return taskData;
		}

	}
}
