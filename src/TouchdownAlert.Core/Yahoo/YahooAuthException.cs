namespace TouchdownAlert.Core.Yahoo;

/// <summary>
/// Thrown when a Yahoo league is polled without a valid login (no token yet, or refresh failed). Callers
/// (the poller) should surface <see cref="Exception.Message"/> as the league's per-poll error rather than
/// treating it as a transient failure - it will not resolve itself without the user logging in again at
/// /setup/yahoo.
/// </summary>
public sealed class YahooAuthException : Exception
{
    public YahooAuthException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
