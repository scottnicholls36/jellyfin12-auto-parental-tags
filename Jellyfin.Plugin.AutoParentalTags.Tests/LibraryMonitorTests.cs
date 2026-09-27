using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AutoParentalTags.Configuration;
using Jellyfin.Plugin.AutoParentalTags.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests;

/// <summary>
/// Collection definition for tests that use Plugin.Instance.
/// Ensures tests run sequentially to avoid race conditions with static state.
/// </summary>
[CollectionDefinition("Plugin Instance Tests", DisableParallelization = true)]
public class PluginInstanceTestCollection
{
}

/// <summary>
/// Tests for the LibraryMonitor class.
/// </summary>
[Collection("Plugin Instance Tests")]
public class LibraryMonitorTests : IAsyncLifetime
{
    /// <summary>
    /// Initializes the test by clearing any existing plugin instance.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task InitializeAsync()
    {
        ClearPluginInstance();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the test by clearing the plugin instance.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task DisposeAsync()
    {
        ClearPluginInstance();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests that LibraryMonitor can be instantiated.
    /// </summary>
    [Fact]
    public void Constructor_ShouldCreateInstance()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());

        // Act
        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        // Assert
        Assert.NotNull(monitor);
    }

    /// <summary>
    /// Tests that Run returns early when plugin is not configured.
    /// </summary>
    [Fact]
    public async Task Run_WhenPluginNotConfigured_ShouldReturnEarly()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = false,
            ProcessOnLibraryScan = false,
            ApiKey = string.Empty
        });

        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var aiServiceFactory = new AiServiceFactory(NullLoggerFactory.Instance);
        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            aiServiceFactory);
        var progress = new Mock<IProgress<double>>();

        // Act
        await monitor.Run(progress.Object, CancellationToken.None);

        // Assert
        mockLibraryManager.Verify(x => x.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    /// <summary>
    /// Tests that Run returns when API key missing.
    /// </summary>
    [Fact]
    public async Task Run_WhenApiKeyMissing_ShouldSkipProcessing()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = true,
            ProcessOnLibraryScan = true,
            ApiKey = string.Empty
        });

        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);
        var progress = new Mock<IProgress<double>>();

        // Act
        await monitor.Run(progress.Object, CancellationToken.None);

        // Assert
        mockLibraryManager.Verify(x => x.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    /// <summary>
    /// Tests that Run processes movies when configured.
    /// </summary>
    [Fact]
    public async Task Run_WhenConfigured_ShouldProcessMovies()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = true,
            ProcessOnLibraryScan = true,
            ApiKey = "key",
            OverwriteExistingTags = false
        });

        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>()); // no movies to avoid external calls

        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var aiServiceFactory = new AiServiceFactory(NullLoggerFactory.Instance);

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            aiServiceFactory);
        var progress = new Mock<IProgress<double>>();

        // Act
        await monitor.Run(progress.Object, CancellationToken.None);

        // Assert
        mockLibraryManager.Verify(x => x.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    /// <summary>
    /// Tests that Run processes returned movies and reports progress.
    /// </summary>
    [Fact]
    public async Task Run_WhenMoviesAvailable_ShouldProcessAndReport()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = true,
            ProcessOnLibraryScan = true,
            ApiKey = "key",
            OverwriteExistingTags = true,
            Provider = AiProvider.Gemini
        });

        var movies = new List<BaseItem>
        {
            new TestMovie { Name = "Movie 1" },
            new TestMovie { Name = "Movie 2" }
        };

        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(movies);

        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(NullLoggerFactory.Instance);
        var aiService = new StubAiService("teens");
        mockAiServiceFactory.Setup(x => x.CreateService(It.IsAny<PluginConfiguration>()))
            .Returns(aiService);

        var progressReports = new List<double>();
        var progress = new Progress<double>(p => progressReports.Add(p));

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object,
            TimeSpan.Zero);

        // Act
        await monitor.Run(progress, CancellationToken.None);
        await Task.Delay(10);

        // Assert
        Assert.Equal(2, aiService.Calls);
        Assert.True(progressReports.Count >= 1);
        Assert.Equal(100, progressReports.Last());
        Assert.All(movies.OfType<TestMovie>(), m => Assert.Contains("teens", m.Tags));
    }

    /// <summary>
    /// Tests that ProcessItemAsync handles movie with existing tags.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WithExistingTag_ShouldSkipWhenNotOverwriting()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Tags = new[] { "kids" }
        };

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, false, CancellationToken.None);

        // Assert
        mockAiService.Verify(
            x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()),
            Times.Never);
    }

    /// <summary>
    /// Tests that ProcessItemAsync processes movie without existing tags.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WithoutExistingTag_ShouldCallAiService()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync("teens");

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Overview = "A test movie",
            OfficialRating = "PG-13",
            Tags = Array.Empty<string>()
        };

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, false, CancellationToken.None);

        // Assert
        mockAiService.Verify(
            x => x.DetermineTargetAudienceAsync(
                "Test Movie",
                2020,
                "A test movie",
                "PG-13",
                It.IsAny<string[]?>(),
                TitleType.Movie),
            Times.Once);
    }

    /// <summary>
    /// Tests that ProcessItemAsync adds tag to movie.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WhenAiReturnsTag_ShouldAddTagToMovie()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync("adults");

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Tags = Array.Empty<string>()
        };

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, false, CancellationToken.None);

        // Assert
        Assert.Contains("adults", movie.Tags);
    }

    /// <summary>
    /// Tests that ProcessItemAsync removes old tags when overwriting.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WithOverwriteTrue_ShouldReplaceExistingTag()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync("adults");

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Tags = new[] { "kids", "family" }
        };

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, true, CancellationToken.None);

        // Assert
        Assert.Contains("adults", movie.Tags);
        Assert.DoesNotContain("kids", movie.Tags);
        Assert.Contains("family", movie.Tags); // Non-audience tag should remain
    }

    /// <summary>
    /// Tests that ProcessItemAsync handles null response from AI.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WhenAiReturnsNull_ShouldNotAddTag()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync((string?)null);

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Tags = Array.Empty<string>()
        };

        var initialTagCount = movie.Tags.Length;

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, false, CancellationToken.None);

        // Assert
        Assert.Equal(initialTagCount, movie.Tags.Length);
    }

    /// <summary>
    /// Tests that ProcessItemAsync handles empty string response from AI.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WhenAiReturnsEmpty_ShouldNotAddTag()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync(string.Empty);

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Tags = Array.Empty<string>()
        };

        var initialTagCount = movie.Tags.Length;

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, false, CancellationToken.None);

        // Assert
        Assert.Equal(initialTagCount, movie.Tags.Length);
    }

    /// <summary>
    /// Tests that ProcessItemAsync does not add duplicate tags.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WithExistingIdenticalTag_ShouldNotAddDuplicate()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var mockLogger = new Mock<ILogger<LibraryMonitor>>();
        var mockAiServiceFactory = new Mock<AiServiceFactory>(Mock.Of<ILoggerFactory>());
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync("kids");

        var monitor = new LibraryMonitor(
            mockLibraryManager.Object,
            mockLogger.Object,
            mockAiServiceFactory.Object);

        var movie = new TestMovie
        {
            Name = "Test Movie",
            ProductionYear = 2020,
            Tags = new[] { "kids" }
        };

        // Act
        await monitor.ProcessItemAsync(movie, mockAiService.Object, true, CancellationToken.None);

        // Assert
        Assert.Single(movie.Tags);
        Assert.Equal("kids", movie.Tags[0]);
    }

    /// <summary>
    /// Tests that TV series are queried and classified as series when enabled.
    /// </summary>
    [Fact]
    public async Task Run_WhenTvShowsEnabled_ShouldTagSeries()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = true,
            ProcessOnLibraryScan = true,
            ProcessTvShows = true,
            ApiKey = "key"
        });

        var movie = new TestMovie { Name = "Movie" };
        var series = new TestSeries { Name = "Series" };
        InternalItemsQuery? capturedQuery = null;
        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new List<BaseItem> { movie, series });

        var aiService = new StubAiService("kids");
        var monitor = CreateMonitor(mockLibraryManager.Object, aiService);

        // Act
        await monitor.Run(new Progress<double>(), CancellationToken.None);

        // Assert
        Assert.NotNull(capturedQuery);
        Assert.Contains(BaseItemKind.Series, capturedQuery!.IncludeItemTypes);
        Assert.Contains("kids", movie.Tags);
        Assert.Contains("kids", series.Tags);
        Assert.Equal(new[] { TitleType.Movie, TitleType.Series }, aiService.TitleTypes);
    }

    /// <summary>
    /// Tests that TV series are skipped when TV show processing is disabled.
    /// </summary>
    [Fact]
    public async Task Run_WhenTvShowsDisabled_ShouldOnlyTagMovies()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = true,
            ProcessOnLibraryScan = true,
            ProcessTvShows = false,
            ApiKey = "key"
        });

        var movie = new TestMovie { Name = "Movie" };
        var series = new TestSeries { Name = "Series" };
        InternalItemsQuery? capturedQuery = null;
        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new List<BaseItem> { movie, series });

        var aiService = new StubAiService("teens");
        var monitor = CreateMonitor(mockLibraryManager.Object, aiService);

        // Act
        await monitor.Run(new Progress<double>(), CancellationToken.None);

        // Assert
        Assert.Equal(new[] { BaseItemKind.Movie }, capturedQuery!.IncludeItemTypes);
        Assert.Contains("teens", movie.Tags);
        Assert.DoesNotContain("teens", series.Tags);
        Assert.Equal(1, aiService.Calls);
    }

    /// <summary>
    /// Tests that a manual run ignores the automatic tagging and library scan settings.
    /// </summary>
    [Fact]
    public async Task RunManualAsync_WhenLibraryScanProcessingDisabled_ShouldStillProcess()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            EnableAutoTagging = false,
            ProcessOnLibraryScan = false,
            ApiKey = "key"
        });

        var movie = new TestMovie { Name = "Movie" };
        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { movie });

        var aiService = new StubAiService("adults");
        var monitor = CreateMonitor(mockLibraryManager.Object, aiService);

        // Act
        await monitor.Run(new Progress<double>(), CancellationToken.None);
        var callsAfterScan = aiService.Calls;
        await monitor.RunManualAsync(new Progress<double>(), CancellationToken.None);

        // Assert
        Assert.Equal(0, callsAfterScan);
        Assert.Equal(1, aiService.Calls);
        Assert.Contains("adults", movie.Tags);
    }

    /// <summary>
    /// Tests that a manual run does nothing when the plugin is not loaded.
    /// </summary>
    [Fact]
    public async Task RunManualAsync_WhenPluginNotLoaded_ShouldReturnEarly()
    {
        // Arrange
        var mockLibraryManager = new Mock<ILibraryManager>();
        var monitor = CreateMonitor(mockLibraryManager.Object, new StubAiService("kids"));
        var progressReports = new List<double>();

        // Act
        await monitor.RunManualAsync(new SyncProgress(progressReports), CancellationToken.None);

        // Assert
        mockLibraryManager.Verify(x => x.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
        Assert.Equal(new[] { 100d }, progressReports);
    }

    /// <summary>
    /// Tests that LocalAI can run without an API key.
    /// </summary>
    [Fact]
    public async Task RunManualAsync_WithLocalAiAndNoApiKey_ShouldProcess()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration
        {
            Provider = AiProvider.LocalAI,
            ApiKey = string.Empty
        });

        var movie = new TestMovie { Name = "Movie" };
        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { movie });

        var aiService = new StubAiService("kids");
        var monitor = CreateMonitor(mockLibraryManager.Object, aiService);

        // Act
        await monitor.RunManualAsync(new Progress<double>(), CancellationToken.None);

        // Assert
        Assert.Equal(1, aiService.Calls);
    }

    /// <summary>
    /// Tests that a second run is skipped while one is already in progress.
    /// </summary>
    [Fact]
    public async Task RunManualAsync_WhenAlreadyRunning_ShouldSkipSecondRun()
    {
        // Arrange
        SetPluginInstance(new PluginConfiguration { ApiKey = "key" });

        var mockLibraryManager = new Mock<ILibraryManager>();
        mockLibraryManager.Setup(x => x.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { new TestMovie { Name = "Movie" } });

        var aiService = new BlockingAiService();
        var monitor = CreateMonitor(mockLibraryManager.Object, aiService);

        // Act
        var firstRun = monitor.RunManualAsync(new Progress<double>(), CancellationToken.None);
        await aiService.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.RunManualAsync(new Progress<double>(), CancellationToken.None);
        aiService.Release.SetResult();
        await firstRun.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(1, aiService.Calls);
        mockLibraryManager.Verify(x => x.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    /// <summary>
    /// Tests that a TV series is classified with the series prompt type.
    /// </summary>
    [Fact]
    public async Task ProcessItemAsync_WithSeries_ShouldRequestSeriesClassification()
    {
        // Arrange
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(x => x.DetermineTargetAudienceAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<TitleType>()))
            .ReturnsAsync("teens");

        var monitor = CreateMonitor(Mock.Of<ILibraryManager>(), new StubAiService("kids"));
        var series = new TestSeries
        {
            Name = "Test Series",
            ProductionYear = 2005,
            Tags = Array.Empty<string>()
        };

        // Act
        await monitor.ProcessItemAsync(series, mockAiService.Object, false, CancellationToken.None);

        // Assert
        mockAiService.Verify(
            x => x.DetermineTargetAudienceAsync(
                "Test Series",
                2005,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                TitleType.Series),
            Times.Once);
        Assert.Contains("teens", series.Tags);
    }

    private static LibraryMonitor CreateMonitor(ILibraryManager libraryManager, IAiService aiService)
    {
        var mockAiServiceFactory = new Mock<AiServiceFactory>(NullLoggerFactory.Instance);
        mockAiServiceFactory.Setup(x => x.CreateService(It.IsAny<PluginConfiguration>()))
            .Returns(aiService);

        return new LibraryMonitor(
            libraryManager,
            NullLogger<LibraryMonitor>.Instance,
            mockAiServiceFactory.Object,
            TimeSpan.Zero);
    }

    private static void ClearPluginInstance()
    {
        var instanceProperty = typeof(Plugin).GetProperty(
            "Instance",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        instanceProperty?.SetValue(null, null);
    }

    private static void SetPluginInstance(PluginConfiguration config)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-tests");
        Directory.CreateDirectory(tempDir);

        var mockPaths = new Mock<IApplicationPaths>();
        mockPaths.Setup(x => x.PluginsPath).Returns(tempDir);
        mockPaths.Setup(x => x.PluginConfigurationsPath).Returns(tempDir);

        var mockSerializer = new Mock<IXmlSerializer>();
        mockSerializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
            .Returns(config);
        mockSerializer.Setup(x => x.SerializeToFile(It.IsAny<PluginConfiguration>(), It.IsAny<string>()));

        var mockLogger = new Mock<ILogger<Plugin>>();

        ClearPluginInstance();
        _ = new Plugin(mockPaths.Object, mockSerializer.Object, mockLogger.Object);
    }
}

/// <summary>
/// Test double for Movie that skips repository calls.
/// </summary>
internal class TestMovie : Movie
{
    public override Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test double for Series that skips repository calls.
/// </summary>
internal class TestSeries : Series
{
    public override Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Simple AI service stub for tests.
/// </summary>
internal sealed class StubAiService : IAiService
{
    private readonly string _tag;

    public StubAiService(string tag)
    {
        _tag = tag;
    }

    public int Calls { get; private set; }

    public List<TitleType> TitleTypes { get; } = new();

    public void Dispose()
    {
    }

    public void SetApiKey(string apiKey)
    {
    }

    public void SetEndpoint(string endpoint)
    {
    }

    public void SetModelName(string modelName)
    {
    }

    public Task<string?> DetermineTargetAudienceAsync(string title, int? year, string? overview, string? officialRating, string[]? genres, TitleType titleType)
    {
        Calls++;
        TitleTypes.Add(titleType);
        return Task.FromResult<string?>(_tag);
    }

    public Task<string[]> GetAvailableModelsAsync()
    {
        return Task.FromResult(Array.Empty<string>());
    }
}

/// <summary>
/// Progress reporter that records values synchronously.
/// </summary>
internal sealed class SyncProgress : IProgress<double>
{
    private readonly List<double> _reports;

    public SyncProgress(List<double> reports)
    {
        _reports = reports;
    }

    public void Report(double value)
    {
        _reports.Add(value);
    }
}

/// <summary>
/// AI service stub that blocks until released, for testing overlapping runs.
/// </summary>
internal sealed class BlockingAiService : IAiService
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Calls { get; private set; }

    public void Dispose()
    {
    }

    public void SetApiKey(string apiKey)
    {
    }

    public void SetEndpoint(string endpoint)
    {
    }

    public void SetModelName(string modelName)
    {
    }

    public async Task<string?> DetermineTargetAudienceAsync(string title, int? year, string? overview, string? officialRating, string[]? genres, TitleType titleType)
    {
        Calls++;
        Started.TrySetResult();
        await Release.Task.ConfigureAwait(false);
        return "kids";
    }

    public Task<string[]> GetAvailableModelsAsync()
    {
        return Task.FromResult(Array.Empty<string>());
    }
}
