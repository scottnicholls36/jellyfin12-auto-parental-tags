using Jellyfin.Plugin.AutoParentalTags.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests.Api;

/// <summary>
/// Tests for the TaskController class.
/// </summary>
public class TaskControllerTests
{
    /// <summary>
    /// Tests that Run queues the Auto Parental Tags scheduled task.
    /// </summary>
    [Fact]
    public void Run_ShouldQueueScheduledTask()
    {
        // Arrange
        var mockTaskManager = new Mock<ITaskManager>();
        var controller = new TaskController(mockTaskManager.Object, NullLogger<TaskController>.Instance);

        // Act
        var result = controller.Run();

        // Assert
        Assert.IsType<NoContentResult>(result);
        mockTaskManager.Verify(x => x.QueueScheduledTask<AutoParentalTagsScheduledTask>(), Times.Once);
    }
}
