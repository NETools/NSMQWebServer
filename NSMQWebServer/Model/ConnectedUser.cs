using NSMQWebServer.Websockets;
using NSQM.Data.Model.Persistence;

namespace NSMQWebServer.Model
{
	public class ConnectedUser
	{
		public ApiClient Connection { get; set; }
		public User User { get; set; }

	}
}
