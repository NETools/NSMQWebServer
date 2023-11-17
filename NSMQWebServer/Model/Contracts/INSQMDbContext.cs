using Microsoft.EntityFrameworkCore;

namespace NSMQWebServer.Model.Contracts
{
	public interface INSQMDbContext<T> where T : DbContext
	{
		public T Context { get; set; }
	}
}
