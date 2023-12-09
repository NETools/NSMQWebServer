using BlazorSessionScopedContainer.Contracts.Sessions.Auth;
using BlazorSessionScopedContainer.Core.SessionManager.MigrationContext.Synchronization;
using Microsoft.EntityFrameworkCore;
using NSMQWebServer.Model;
using NSMQWebServer.Model.Contracts;
using NSMQWebServer.Websockets;
using NSQM.Data.Extensions;
using NSQM.Data.Messages;
using NSQM.Data.Model.Persistence;
using NSQM.Data.Model.Response;
using System.Diagnostics.CodeAnalysis;
using Channel = NSQM.Data.Model.Persistence.Channel;

namespace NSMQWebServer.Persistence
{
	public class MessageQueueDbContext : DbContext
	{
		private NSMutex _mutex;

		public DbSet<Channel> Channels { get; set; }
		public DbSet<User> Users { get; set; }
		public DbSet<TaskData> TaskDatas { get; set; }
		public string DbPath { get; }

		public MessageQueueDbContext()
		{
			DbPath = "C:\\Users\\enesh\\source\\repos\\NSMQWebServer\\NSMQWebServer\\database.db";
			_mutex = new NSMutex();
		}

		protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite($"Data Source={DbPath}");
		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Channel>()
						.Navigation(e => e.Users)
						.UsePropertyAccessMode(PropertyAccessMode.Property)
						.AutoInclude();

			modelBuilder.Entity<User>()
					.Navigation(e => e.TaskDatas)
					.UsePropertyAccessMode(PropertyAccessMode.Property)
			.AutoInclude();

			modelBuilder.Entity<Channel>()
			   .HasMany(e => e.Users)
			   .WithMany();

			modelBuilder.Entity<User>()
				.HasMany(e => e.TaskDatas)
				.WithMany();
		}

		public async Task<User?> FindUser(Guid userId)
		{
			using (_mutex.GetLock())
			{
				return await Users
				.Include(p => p.TaskDatas)
				.Where(p => p.UserId == userId)
				.FirstOrDefaultAsync();
			}
		}

		public async Task<Channel?> FindChannel(string channelId)
		{
			using (_mutex.GetLock())
			{
				return await Channels
				.Include(p => p.Users)
				.ThenInclude(p => p.TaskDatas)
				.Where(p => p.ChannelId == channelId)
				.FirstOrDefaultAsync();
			}
		}
		public IQueryable<Channel> FindChannelQuery(string channelId)
		{
			using (_mutex.GetLock())
			{
				return Channels
				.Include(p => p.Users)
				.ThenInclude(p => p.TaskDatas)
				.Where(p => p.ChannelId == channelId);
			}
		}

		public async Task<TaskData?> FindTask(Guid taskId)
		{
			using (_mutex.GetLock())
			{
				return await TaskDatas
				.Where(p => p.TaskId == taskId)
				.FirstOrDefaultAsync();
			}
		}

		public async Task<TaskData?> FindTaskPhased(Guid phaseId)
		{
			using (_mutex.GetLock())
			{
				return await TaskDatas
				.Where(p => p.PhaseId == phaseId)
				.FirstOrDefaultAsync();
			}
		}
		public async Task<ApiResponseL1<Channel>> CreateChannel(string channelId)
		{
			using (_mutex.GetLock())
			{
				var channel = await FindChannel(channelId);
				if (channel != null)
				{
					return ApiResponseL1<Channel>.Ok(channel, $"Channel with the id {channelId} already exists.", ApiResponseL1.ChannelExists);
				}

				var channelModel = new Channel()
				{
					Users = new List<User>(),
					ChannelId = channelId
				};

				var entity = await Channels.AddAsync(channelModel);

				if (await Save())
					return ApiResponseL1<Channel>.Ok(entity, $"Channel with the id {channelId} created.", ApiResponseL1.ChannelCreated);
				return ApiResponseL1<Channel>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}
		public async Task<ApiResponseL1<User>> CreateUser(Guid userId, UserType userType)
		{
			using (_mutex.GetLock())
			{
				var user = await FindUser(userId);
				if (user != null)
				{
					return ApiResponseL1<User>.Ok(user, $"User with the id {userId} already exists.", ApiResponseL1.UserExists);
				}

				var userModel = new User()
				{
					TaskDatas = new List<TaskData>(),
					UserId = userId,
					UserType = userType
				};

				var entity = await Users.AddAsync(userModel);


				if (await Save())
					return ApiResponseL1<User>.Ok(entity, $"User with the id {userId} created.", ApiResponseL1.UserCreated);
				return ApiResponseL1<User>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task<ApiResponseL1<TaskData>> CreateTaskData(TaskData taskData)
		{
			using (_mutex.GetLock())
			{
				var channel = await FindChannel(taskData.ChannelId);
				if (channel == null)
				{
					return ApiResponseL1<TaskData>.Failed($"Could not find channel with id {taskData.ChannelId}.", ApiResponseL1.ChannelNotFound);
				}

				var fTaskData = await FindTask(taskData.TaskId);
				if (fTaskData != null && fTaskData.PhaseId == taskData.PhaseId)
				{
					return ApiResponseL1<TaskData>.Ok(fTaskData, $"Task with the phase id {fTaskData.PhaseId} already added to the channel with id {fTaskData.ChannelId}.", ApiResponseL1.TaskDataExists);
				}

				var entity = await TaskDatas.AddAsync(taskData);
				if (await Save())
					return ApiResponseL1<TaskData>.Ok(entity, $"TaskData with the id {entity.Entity.TaskId} added to channel with id {entity.Entity.ChannelId}.", ApiResponseL1.TaskDataCreated);
				return ApiResponseL1<TaskData>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task<ApiResponseL1<User>> AddUserToChannel(string channelId, Guid userId)
		{
			using (_mutex.GetLock())
			{
				var channel = await FindChannel(channelId);
				if (channel == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find channel with id {channelId}.", ApiResponseL1.ChannelNotFound);
				}

				var user = await FindUser(userId);
				if (user == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find user with id {userId}.", ApiResponseL1.UserNotFound);
				}

				if (channel.Users == null)
				{
					return ApiResponseL1<User>.Failed($"Entity fatal error -- list not instanciated.", ApiResponseL1.FatalInstanceError);
				}

				if (channel.Users.Any(p => p.UserId == userId))
				{
					return ApiResponseL1<User>.Ok(user, $"User with the id {userId} already added to channel with id {channelId}.", ApiResponseL1.UserAlreadyAdded);
				}

				channel.Users.Add(user);

				if (await Save())
					return ApiResponseL1<User>.Ok(user, $"User with id {userId} added to channel with id {channelId}.", ApiResponseL1.UserAddedToChannel);
				return ApiResponseL1<User>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task<ApiResponseL1<User>> AddTaskToUser(Guid userId, Guid taskId)
		{
			using (_mutex.GetLock())
			{
				var user = await FindUser(userId);
				if (user == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find user with id {userId}.", ApiResponseL1.UserNotFound);
				}

				var taskData = await FindTaskPhased(taskId);
				if (taskData == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find task with id {taskId}.", ApiResponseL1.TaskDataNotFound);
				}

				var channel = await FindChannel(taskData.ChannelId);
				if (channel == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find channel with id {taskData.ChannelId} -- assigning task requires a channel to exist.", ApiResponseL1.ChannelNotFound);
				}

				if (user.TaskDatas == null)
				{
					return ApiResponseL1<User>.Failed($"Entity fatal error -- list not instanciated.", ApiResponseL1.FatalInstanceError);
				}

				if (user.TaskDatas.Any(p => p.TaskId == taskId))
				{
					return ApiResponseL1<User>.Ok(user, $"Task with id {taskId} already added to user with id {userId}.", ApiResponseL1.TaskDataAlreadyAdded);
				}

				user.TaskDatas.Add(taskData);

				if (await Save())
					return ApiResponseL1<User>.Ok(user, "Task added to user.", ApiResponseL1.TaskDataAddedToUser);
				return ApiResponseL1<User>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task<ApiResponseL1<User>> RemoveTaskFromUser(Guid userId, Guid taskId)
		{
			using (_mutex.GetLock())
			{
				var user = await FindUser(userId);
				if (user == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find user with id {userId}.", ApiResponseL1.UserNotFound);
				}

				var taskData = await FindTask(taskId);
				if (taskData == null)
				{
					return ApiResponseL1<User>.Failed($"Could not find task with id {taskId}.", ApiResponseL1.TaskDataNotFound);
				}

				if (user.TaskDatas == null)
				{
					return ApiResponseL1<User>.Failed($"Entity fatal error -- list not instanciated.", ApiResponseL1.FatalInstanceError);
				}

				if (!user.TaskDatas.Any(p => p.TaskId == taskId))
				{
					return ApiResponseL1<User>.Ok(user, $"Task with id {taskId} was not added to user with id {userId}.", ApiResponseL1.TaskDataNotAddedToUser);
				}

				user.TaskDatas.Remove(taskData);

				if (await Save())
					return ApiResponseL1<User>.Ok(user, "Task removed from user.", ApiResponseL1.TaskDataRemovedFromUser);
				return ApiResponseL1<User>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task<ApiResponseL1<Channel>> RemoveUserFromChannel(string channelId, Guid userId)
		{
			using (_mutex.GetLock())
			{
				var channel = await FindChannel(channelId);
				if (channel == null)
				{
					return ApiResponseL1<Channel>.Failed($"Could not find channel with id {channelId}.", ApiResponseL1.ChannelNotFound);
				}

				var user = await FindUser(userId);
				if (user == null)
				{
					return ApiResponseL1<Channel>.Failed($"Could not find user with id {userId}.", ApiResponseL1.UserNotFound);
				}

				if (channel.Users == null)
				{
					return ApiResponseL1<Channel>.Failed($"Entity fatal error -- list not instanciated.", ApiResponseL1.FatalInstanceError);
				}

				if (!channel.Users.Any(p => p.UserId == userId))
				{
					return ApiResponseL1<Channel>.Ok(channel, $"User with id {userId} was not added to channel with id {channelId}.", ApiResponseL1.UserNotAddedToChannel);
				}

				channel.Users.Remove(user);

				if (await Save())
					return ApiResponseL1<Channel>.Ok(channel, $"User with id {userId} is removed from channel with id {channelId}.", ApiResponseL1.UserRemovedFromChannel);
				return ApiResponseL1<Channel>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}
		public async Task<ApiResponseL1<TaskData>> RemoveTaskData(Guid taskId)
		{
			using (_mutex.GetLock())
			{
				var taskData = await FindTask(taskId);
				if (taskData == null)
				{
					return ApiResponseL1<TaskData>.Failed($"Task with id {taskId} was not found.", ApiResponseL1.TaskDataNotFound);
				}

				TaskDatas.RemoveRange(await TaskDatas.Where(p => p.TaskId == taskId).ToArrayAsync());

				if (await Save())
					return ApiResponseL1<TaskData>.Ok(taskData, $"Task with id {taskId} is removed.", ApiResponseL1.TaskDataRemoved);
				return ApiResponseL1<TaskData>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task<ApiResponseL1<User>> RemoveUser(Guid userId, Action<IEnumerable<TaskData>> callback = null)
		{
			using (_mutex.GetLock())
			{
				var user = await FindUser(userId);
				if (user == null)
				{
					return ApiResponseL1<User>.Failed($"User with id {userId} was not found.", ApiResponseL1.UserNotFound);
				}

				if (user.TaskDatas == null)
				{
					return ApiResponseL1<User>.Failed($"Entity fatal error -- list not instanciated.", ApiResponseL1.FatalInstanceError);
				}

				if (user.TaskDatas.Count > 0)
				{
					if (callback != null)
					{
						callback?.Invoke(user.TaskDatas);
						return ApiResponseL1<User>.Ok(user, "Callback called.", ApiResponseL1.IllegalUserDeletion_Callback);
					}
					return ApiResponseL1<User>.Ok(user, $"User with id {userId} is not removed -- user has task(s) {user.TaskDatas.Count} tasks open.", ApiResponseL1.IllegalUserDeletion_NoCallback);
				}

				var entry = Users.Remove(user);

				if (await Save())
					return ApiResponseL1<User>.Ok(entry, $"User with id {userId} is removed.", ApiResponseL1.UserRemoved);
				return ApiResponseL1<User>.Failed($"Fatal error -- see exception.", ApiResponseL1.FatalSqlError);
			}
		}

		public async Task ForAllEqualUser(string channelId, UserType userType, [NotNull] Action<Channel, User> action)
		{
			using (_mutex.GetLock())
			{
				await FindChannelQuery(channelId)
					.SelectMany(p => p.Users, (channel, user) => new { Channel = channel, User = user })
					.Where(p => p.User != null && p.User.UserType == userType)
					.ForEachAsync(data => action(data.Channel, data.User));
			}
		}

		public async Task ForAllOppositeUser(string channelId, UserType userType, [NotNull] Action<Channel, User> action)
		{
			using (_mutex.GetLock())
			{
				await FindChannelQuery(channelId)
					.SelectMany(p => p.Users, (channel, user) => new { Channel = channel, User = user })
					.Where(p => p.User != null && p.User.UserType != userType)
					.ForEachAsync(data => action(data.Channel, data.User));
			}
		}

		private async Task<bool> Save()
		{
			using (_mutex.GetLock())
			{
				try
				{
					var count = await SaveChangesAsync(true);

					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"Saved changes - {count} entries modified.");
					Console.ForegroundColor = ConsoleColor.Gray;

					return true;
				}
				catch (Exception ex)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(ex.Message);
					Console.WriteLine(ex.StackTrace);
					Console.ForegroundColor = ConsoleColor.DarkRed;
					Console.WriteLine(ex.InnerException?.Message);
					Console.WriteLine(ex.InnerException?.StackTrace);
					Console.ForegroundColor = ConsoleColor.Gray;

					return false;
				}
			}
		}
	}
}