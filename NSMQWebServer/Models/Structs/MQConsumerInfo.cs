using NSMQWebServer.Websockets;

namespace NSMQWebServer.Models.Structs
{
    public struct MQConsumerInfo
    {
        public ApiClient Client;
        public string ConsumerName;
        public int ConsumerPriority;
    }
}
