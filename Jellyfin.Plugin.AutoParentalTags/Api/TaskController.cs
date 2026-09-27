using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoParentalTags.Api;

/// <summary>
/// API controller for running Auto Parental Tags on demand.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("AutoParentalTags")]
public class TaskController : ControllerBase
{
    private readonly ITaskManager _taskManager;
    private readonly ILogger<TaskController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskController"/> class.
    /// </summary>
    /// <param name="taskManager">Instance of the <see cref="ITaskManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TaskController}"/> interface.</param>
    public TaskController(
        ITaskManager taskManager,
        ILogger<TaskController> logger)
    {
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <summary>
    /// Queues the Auto Parental Tags scheduled task, independent of Jellyfin's library scan.
    /// </summary>
    /// <returns>No content once the task is queued.</returns>
    [HttpPost("Run")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Run()
    {
        _logger.LogInformation("Auto Parental Tags run requested from the configuration page");
        _taskManager.QueueScheduledTask<AutoParentalTagsScheduledTask>();
        return NoContent();
    }
}
