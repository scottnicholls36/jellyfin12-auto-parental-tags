namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// The kind of library item being classified.
/// </summary>
public enum TitleType
{
    /// <summary>
    /// A movie.
    /// </summary>
    Movie,

    /// <summary>
    /// A TV series, classified as a whole.
    /// </summary>
    Series
}
