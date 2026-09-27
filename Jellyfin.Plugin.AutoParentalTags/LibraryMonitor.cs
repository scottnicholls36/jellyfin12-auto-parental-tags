using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AutoParentalTags.Configuration;
using Jellyfin.Plugin.AutoParentalTags.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoParentalTags;

/// <summary>
/// Monitors library changes and processes movies and TV series.
/// </summary>
public class LibraryMonitor : ILibraryPostScanTask
{
    // Shared across instances: Jellyfin may create the post-scan task separately from the scheduled task's instance
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryMonitor> _logger;
    private readonly AiServiceFactory _aiServiceFactory;
    private readonly TimeSpan _processingDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryMonitor"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LibraryMonitor}"/> interface.</param>
    /// <param name="aiServiceFactory">Instance of the <see cref="AiServiceFactory"/> class.</param>
    /// <param name="processingDelay">Optional delay between processing items.</param>
    public LibraryMonitor(
        ILibraryManager libraryManager,
        ILogger<LibraryMonitor> logger,
        AiServiceFactory aiServiceFactory,
        TimeSpan? processingDelay = null)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _aiServiceFactory = aiServiceFactory;
        _processingDelay = processingDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Sanitizes a string for logging to prevent log forging attacks.
    /// </summary>
    /// <param name="value">The value to sanitize.</param>
    /// <returns>A sanitized string safe for logging.</returns>
    private static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = LoadConfiguration();
        if (config == null || !config.EnableAutoTagging || !config.ProcessOnLibraryScan)
        {
            _logger.LogDebug("Auto-tagging is disabled or not configured to run on library scan");
            progress?.Report(100);
            return;
        }

        await ProcessLibraryAsync(config, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes the library on demand, regardless of the automatic tagging and library scan settings.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunManualAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = LoadConfiguration();
        if (config == null)
        {
            progress?.Report(100);
            return;
        }

        await ProcessLibraryAsync(config, progress, cancellationToken).ConfigureAwait(false);
    }

    private PluginConfiguration? LoadConfiguration()
    {
        try
        {
            return Plugin.Instance?.Configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load plugin configuration");
            return null;
        }
    }

    private async Task ProcessLibraryAsync(PluginConfiguration config, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // LocalAI instances often run without authentication
        if (config.Provider != AiProvider.LocalAI && string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning("AI API key is not configured");
            progress?.Report(100);
            return;
        }

        // Library scans and manual runs share this path; only one may run at a time
        if (!await RunLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Auto Parental Tags is already running; skipping this run");
            progress?.Report(100);
            return;
        }

        try
        {
            // Create the appropriate AI service
            using var aiService = _aiServiceFactory.CreateService(config);

            var itemTypes = config.ProcessTvShows
                ? new[] { BaseItemKind.Movie, BaseItemKind.Series }
                : new[] { BaseItemKind.Movie };

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = itemTypes,
                IsVirtualItem = false,
                Recursive = true
            }).Where(item => item is Movie || (config.ProcessTvShows && item is Series)).ToList();

            _logger.LogInformation(
                "Found {MovieCount} movies and {SeriesCount} TV series to process",
                items.Count(item => item is Movie),
                items.Count(item => item is Series));

            var processedCount = 0;
            var totalCount = items.Count;

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await ProcessItemAsync(item, aiService, config.OverwriteExistingTags, cancellationToken).ConfigureAwait(false);
                    processedCount++;

                    var progressPercent = (double)processedCount / totalCount * 100;
                    progress?.Report(progressPercent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing '{Title}': {Message}", SanitizeForLog(item.Name), ex.Message);
                }

                // Add a small delay to avoid rate limiting (configurable for testing)
                await Task.Delay(_processingDelay, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Completed processing {Count} items", processedCount);
        }
        finally
        {
            RunLock.Release();
        }

        // Always report 100% completion at the end
        progress?.Report(100);
    }

    /// <summary>
    /// Processes a single movie or TV series to add audience tags.
    /// </summary>
    /// <param name="item">The movie or TV series to process.</param>
    /// <param name="aiService">The AI service to use.</param>
    /// <param name="overwriteExisting">Whether to overwrite existing tags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessItemAsync(
        BaseItem item,
        IAiService aiService,
        bool overwriteExisting,
        CancellationToken cancellationToken = default)
    {
        // Check if the item already has an audience tag
        var existingTags = item.Tags?.Where(
            t => t.Equals("kids", StringComparison.OrdinalIgnoreCase)
                || t.Equals("teens", StringComparison.OrdinalIgnoreCase)
                || t.Equals("adults", StringComparison.OrdinalIgnoreCase)).ToList();

        if (existingTags?.Count > 0 && !overwriteExisting)
        {
            _logger.LogDebug(
                "'{Title}' already has audience tag(s): {Tags}",
                item.Name,
                string.Join(", ", existingTags));
            return;
        }

        // Get item metadata
        var title = item.Name;
        var year = item.ProductionYear;
        var overview = item.Overview;
        var rating = item.OfficialRating;
        var genres = item.Genres?.ToArray();
        var titleType = item is Series ? TitleType.Series : TitleType.Movie;

        // Call AI API
        var audienceTag = await aiService.DetermineTargetAudienceAsync(
            title,
            year,
            overview,
            rating,
            genres,
            titleType).ConfigureAwait(false);

        if (string.IsNullOrEmpty(audienceTag))
        {
            _logger.LogWarning("Could not determine audience for '{Title}'", SanitizeForLog(title));
            return;
        }

        // Remove old audience tags if overwriting
        if (overwriteExisting && existingTags?.Count > 0)
        {
            var tagsList = item.Tags?.ToList() ?? new List<string>();
            foreach (var tag in existingTags)
            {
                tagsList.Remove(tag);
            }

            item.Tags = tagsList.ToArray();
        }

        // Add the new tag
        var currentTags = item.Tags?.ToList() ?? new List<string>();
        if (!currentTags.Contains(audienceTag, StringComparer.OrdinalIgnoreCase))
        {
            currentTags.Add(audienceTag);
            item.Tags = currentTags.ToArray();

            // Save changes
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Added '{Tag}' tag to '{Title}' ({Year})",
                audienceTag,
                SanitizeForLog(title),
                year);
        }
    }
}
