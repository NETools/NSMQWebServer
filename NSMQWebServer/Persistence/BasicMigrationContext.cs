using BlazorSessionScopedContainer.Contracts.Migration;
using BlazorSessionScopedContainer.Contracts.Sessions;
using NSMQWebServer.Model.Contracts;

namespace NSMQWebServer.Persistence
{
	public class BasicMigrationContext : IMigrationContext
	{
		private MessageQueueDbContext _dbContext = new MessageQueueDbContext();

		public T RetrieveData<T>(Dictionary<string, dynamic> arguments, object[] dependencies)
		{
			var instance = (T)Activator.CreateInstance(typeof(T), dependencies);
			if (arguments["SessionType"] != SessionType.Global)
				return instance;

			if (instance is INSQMDbContext<MessageQueueDbContext> dbContext)
			{
				dbContext.Context = _dbContext;
			}

			return instance;
		}

		public void Save<T>(T instance, string propertyName, Dictionary<string, dynamic> arguments)
		{
			throw new NotImplementedException();
		}
	}
}
