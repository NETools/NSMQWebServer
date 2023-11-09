using System.Net;

namespace NSMQWebServer.Models
{
    public class ChannelServiceResult
    {
        public HttpStatusCode StatusCode { get; set; }
        public string Message { get; set; }
    }
}
