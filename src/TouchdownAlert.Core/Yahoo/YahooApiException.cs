using System.Net;

namespace TouchdownAlert.Core.Yahoo;

/// <summary>Thrown when a call to the Yahoo Fantasy read API fails (transport error, non-success status, or malformed XML).</summary>
public sealed class YahooApiException : Exception
{
    public YahooApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code Yahoo returned, if the failure was an unsuccessful response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
