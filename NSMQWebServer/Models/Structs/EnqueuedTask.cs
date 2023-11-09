using NSQM.Data.Messages;

namespace NSMQWebServer.Models.Structs
{
    public struct EnqueuedTask
    {
        public int Index;
        public string PublisherId;
        public NSQMTaskMessage Task;
    }
}
