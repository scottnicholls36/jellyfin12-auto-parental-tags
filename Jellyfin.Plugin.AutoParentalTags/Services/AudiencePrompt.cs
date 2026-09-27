using System.Globalization;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Builds the audience classification prompt shared by all AI services.
/// </summary>
public static class AudiencePrompt
{
    /// <summary>
    /// Builds the prompt asking the AI to classify the target audience of an item.
    /// </summary>
    /// <param name="title">Item title.</param>
    /// <param name="year">Release year, or first air year for a series.</param>
    /// <param name="overview">Item overview/synopsis.</param>
    /// <param name="officialRating">Official rating (if available).</param>
    /// <param name="genres">Item genres.</param>
    /// <param name="titleType">Whether the item is a movie or a TV series.</param>
    /// <returns>The prompt text.</returns>
    public static string Build(
        string title,
        int? year,
        string? overview,
        string? officialRating,
        string[]? genres,
        TitleType titleType)
    {
        var isSeries = titleType == TitleType.Series;

        var examples = isSeries
            ? @"- A TV-PG sitcom from the 1970s might be targeted at adults despite being appropriate for children
- A TV-14 drama might be targeted specifically at teenagers
- An animated series might be targeted at adults despite its cartoon format"
            : @"- A PG movie from the 1970s might be targeted at adults despite being appropriate for children
- A PG-13 action movie might be targeted specifically at teenagers
- An unrated Christmas special might be clearly targeted at kids";

        var considerations = isSeries
            ? @"1. The show's marketing, broadcast slot and intended demographic
2. Themes and subject matter complexity
3. Historical context (older family-rated shows often targeted adults)
4. Whether it's a franchise aimed at kids/teens/adults
5. The sophistication level of storytelling"
            : @"1. The film's marketing and intended demographic
2. Themes and subject matter complexity
3. Historical context (pre-1990 PG films often targeted adults)
4. Whether it's a franchise aimed at kids/teens/adults
5. The sophistication level of storytelling";

        return $@"Analyze this {(isSeries ? "TV series" : "movie")} and determine its TARGET AUDIENCE (not content rating).
Consider that target audience is different from content appropriateness:
{examples}

{(isSeries ? "TV Series" : "Movie")} Information:
Title: {title}
{(isSeries ? "First Aired" : "Year")}: {year?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}
Official Rating: {officialRating ?? "Not Rated"}
Genres: {(genres?.Length > 0 ? string.Join(", ", genres) : "Unknown")}
Overview: {overview ?? "No overview available"}

Respond with ONLY ONE of these three options based on the PRIMARY target audience:
- kids (targeted at children, typically ages 2-11)
- teens (targeted at teenagers, typically ages 12-17)
- adults (targeted at mature audiences, ages 18+)

Consider:
{considerations}

Respond with just one word: kids, teens, or adults";
    }
}
