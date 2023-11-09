
using Microsoft.AspNetCore.Mvc;
using NSMQWebServer.Components;
using NSMQWebServer.Models;
using NSMQWebServer.Services;
using NSQM.Data.Model;
using System.Net;
using System.Text.Json;

namespace NSMQWebServer.Controllers
{
	[ApiController]
	[Route("MessageQueueServices")]
	public class ChannelController : ControllerBase
	{
		public MessageQueueServices MessageQueueServices { get; private set; }

		public ChannelController(MessageQueueServices channelServices)
		{
			MessageQueueServices = channelServices;
		}

		[HttpPost("Channels/Create")]
		public async Task<IActionResult> CreateChannel([FromBody] string channelName)
		{
			var result = await MessageQueueServices.CreateChannel(channelName);

			switch (result.StatusCode)
			{
				case HttpStatusCode.Created:
					return Created($"Channels/{channelName}", new
					{
						ApiResult = "CHANNEL_CREATED",
						ChannelName = channelName,
						Message = $"Channel {channelName} created."
					});
				case HttpStatusCode.BadRequest:
					return BadRequest(new
					{
						ApiResult = "CHANNEL_EXISTS",
						ChannelName = channelName,
						Message = $"Channel {channelName} already exists."
					});
			}

			return BadRequest(new
			{
				ApiResult = "NOT_PARSED",
				Message = $"Internal server error."
			});
		}

		[HttpPost("Channels/{channelName}/CreateTask")]
		public async Task<IActionResult> PublishTask([FromRoute] string channelName, [FromBody] TaskBody taskBody)
		{
			var result = await MessageQueueServices.BroadcastTask(channelName, taskBody.PublisherId, taskBody.TaskName, taskBody.TaskBuffer);
			switch (result.StatusCode)
			{
				case HttpStatusCode.OK:
					return Ok(new
					{
						ApiResult = "TASK_PUBLISHED",
						ChannelName = channelName,
						Message = result.Message
					});
				case HttpStatusCode.NotFound:
					return NotFound(new
					{
						ApiResult = "CHANNEL_NOT_FOUND",
						ChannelName = channelName,
						Message = result.Message
					});
			}

			return BadRequest(new
			{
				ApiResult = "NOT_PARSED",
				Message = $"Internal server error."
			});
		}

	}
}
