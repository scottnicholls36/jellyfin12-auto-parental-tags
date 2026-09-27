using System;
using System.Net;
using Jellyfin.Plugin.AutoParentalTags.Services;
using Xunit;

namespace Jellyfin.Plugin.AutoParentalTags.Tests.Services;

/// <summary>
/// Tests for the AiServiceUnavailableException class.
/// </summary>
public class AiServiceUnavailableExceptionTests
{
    /// <summary>
    /// Tests that errors affecting every request stop the run.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public void IsFatal_WithAccountOrModelError_ShouldReturnTrue(HttpStatusCode statusCode)
    {
        Assert.True(AiServiceUnavailableException.IsFatal(statusCode, "{}"));
    }

    /// <summary>
    /// Tests that transient or per-item errors do not stop the run.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    public void IsFatal_WithTransientError_ShouldReturnFalse(HttpStatusCode statusCode)
    {
        Assert.False(AiServiceUnavailableException.IsFatal(statusCode, "{\"error\": {\"message\": \"try again\"}}"));
    }

    /// <summary>
    /// Tests that Gemini's invalid API key response stops the run.
    /// </summary>
    [Fact]
    public void IsFatal_WithGeminiInvalidApiKey_ShouldReturnTrue()
    {
        const string Body = "{\"error\": {\"code\": 400, \"message\": \"API key not valid.\", \"details\": [{\"reason\": \"API_KEY_INVALID\"}]}}";

        Assert.True(AiServiceUnavailableException.IsFatal(HttpStatusCode.BadRequest, Body));
        Assert.False(AiServiceUnavailableException.IsFatal(HttpStatusCode.BadRequest, null));
    }

    /// <summary>
    /// Tests the exception constructors.
    /// </summary>
    [Fact]
    public void Constructors_ShouldSetMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        Assert.NotNull(new AiServiceUnavailableException().Message);
        Assert.Equal("no credit", new AiServiceUnavailableException("no credit").Message);

        var withInner = new AiServiceUnavailableException("no credit", inner);
        Assert.Equal("no credit", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }
}
