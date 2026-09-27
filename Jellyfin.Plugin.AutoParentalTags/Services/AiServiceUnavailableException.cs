using System;
using System.Net;

namespace Jellyfin.Plugin.AutoParentalTags.Services;

/// <summary>
/// Thrown when the AI provider rejects requests in a way that will affect every item
/// (invalid key, no credit, unknown model), so the run should stop rather than retry each item.
/// </summary>
public class AiServiceUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AiServiceUnavailableException"/> class.
    /// </summary>
    public AiServiceUnavailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiServiceUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AiServiceUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiServiceUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AiServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Determines whether an error response means no further requests can succeed.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="responseBody">The error response body.</param>
    /// <returns><c>true</c> if the run should stop.</returns>
    public static bool IsFatal(HttpStatusCode statusCode, string? responseBody)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => true,
            HttpStatusCode.PaymentRequired => true,
            HttpStatusCode.Forbidden => true,
            HttpStatusCode.NotFound => true,

            // Gemini reports an invalid API key as 400 rather than 401
            HttpStatusCode.BadRequest => responseBody?.Contains("API_KEY_INVALID", StringComparison.Ordinal) == true,
            _ => false
        };
    }
}
