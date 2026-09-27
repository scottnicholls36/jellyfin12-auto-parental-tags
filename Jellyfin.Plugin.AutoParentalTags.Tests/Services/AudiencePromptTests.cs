using Jellyfin.Plugin.AutoParentalTags.Services;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests.Services;

/// <summary>
/// Tests for the AudiencePrompt class.
/// </summary>
public class AudiencePromptTests
{
    /// <summary>
    /// Tests that the movie prompt is unchanged from the original movie-only prompt.
    /// </summary>
    [Fact]
    public void Build_ForMovie_ShouldMatchOriginalPrompt()
    {
        // Arrange
        const string Expected = @"Analyze this movie and determine its TARGET AUDIENCE (not content rating).
Consider that target audience is different from content appropriateness:
- A PG movie from the 1970s might be targeted at adults despite being appropriate for children
- A PG-13 action movie might be targeted specifically at teenagers
- An unrated Christmas special might be clearly targeted at kids

Movie Information:
Title: The Sting
Year: 1973
Official Rating: PG
Genres: Comedy, Crime
Overview: Two grifters team up.

Respond with ONLY ONE of these three options based on the PRIMARY target audience:
- kids (targeted at children, typically ages 2-11)
- teens (targeted at teenagers, typically ages 12-17)
- adults (targeted at mature audiences, ages 18+)

Consider:
1. The film's marketing and intended demographic
2. Themes and subject matter complexity
3. Historical context (pre-1990 PG films often targeted adults)
4. Whether it's a franchise aimed at kids/teens/adults
5. The sophistication level of storytelling

Respond with just one word: kids, teens, or adults";

        // Act
        var prompt = AudiencePrompt.Build("The Sting", 1973, "Two grifters team up.", "PG", new[] { "Comedy", "Crime" }, TitleType.Movie);

        // Assert
        Assert.Equal(Expected, prompt);
    }

    /// <summary>
    /// Tests that the series prompt describes a TV series.
    /// </summary>
    [Fact]
    public void Build_ForSeries_ShouldDescribeTvSeries()
    {
        // Act
        var prompt = AudiencePrompt.Build("Doctor Who", 2005, "A time traveller.", "TV-PG", new[] { "Sci-Fi" }, TitleType.Series);

        // Assert
        Assert.StartsWith("Analyze this TV series and determine its TARGET AUDIENCE", prompt, System.StringComparison.Ordinal);
        Assert.Contains("TV Series Information:", prompt, System.StringComparison.Ordinal);
        Assert.Contains("First Aired: 2005", prompt, System.StringComparison.Ordinal);
        Assert.Contains("Title: Doctor Who", prompt, System.StringComparison.Ordinal);
        Assert.Contains("The show's marketing", prompt, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Movie Information", prompt, System.StringComparison.Ordinal);
        Assert.EndsWith("Respond with just one word: kids, teens, or adults", prompt, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that missing metadata falls back to placeholder text.
    /// </summary>
    [Fact]
    public void Build_WithMissingMetadata_ShouldUseFallbacks()
    {
        // Act
        var prompt = AudiencePrompt.Build("Untitled", null, null, null, null, TitleType.Series);

        // Assert
        Assert.Contains("First Aired: Unknown", prompt, System.StringComparison.Ordinal);
        Assert.Contains("Official Rating: Not Rated", prompt, System.StringComparison.Ordinal);
        Assert.Contains("Genres: Unknown", prompt, System.StringComparison.Ordinal);
        Assert.Contains("Overview: No overview available", prompt, System.StringComparison.Ordinal);
    }
}
