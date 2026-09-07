using System.Net;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>Thrown when a call to the Sleeper public API fails (transport error, non-success status, or malformed JSON).</summary>
public sealed class SleeperApiException : Exception
{
    public SleeperApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code Sleeper returned, if the failure was an unsuccessful response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
