using System.Net;

namespace TouchdownAlert.Core.Espn;

/// <summary>Thrown when a call to the ESPN fantasy API fails (transport error, non-success status, or malformed JSON).</summary>
public sealed class EspnApiException : Exception
{
    public EspnApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code returned by ESPN, if the failure was an unsuccessful response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
