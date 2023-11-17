using BlazorSessionScopedContainer.Core;
using Microsoft.AspNetCore.Mvc;
using NSMQWebServer.Model;
using NSMQWebServer.Services;
using NSQM.Data.Model;
using NSQM.Data.Model.Persistence;
using NSQM.Data.Model.Response;

namespace NSMQWebServer.Controllers
{
	[ApiController]
	[Route("/MQ/API")]
	public class MessageQueueController : ControllerBase
	{
		public NSession Session { get; set; }
		public ChannelServices CoreServices { get; set; }

		public MessageQueueController(NSession session)
		{
			Session = session;
			CoreServices = Session.GetGlobalService<ChannelServices>();
		}

		[HttpPost("/CreateChannel/{ChannelName}")]
		public async Task<IActionResult> CreateChannel([FromRoute] string ChannelName)
		{
			var result = await CoreServices.CreateChannel(ChannelName);
			var httpResponse = new ApiResponseL2<Channel>(result);
			return httpResponse.Accept();
		}

		[HttpPost("/CreateTask/")]
		public async Task<IActionResult> CreateTask([FromBody] TaskData taskData)
		{
			var result = await CoreServices.CreateTask(taskData);
			var httpResponse = new ApiResponseL2<TaskData>(result);
			return httpResponse.Accept();
		}
	}
}
