using System.IO.Compression;
using System.Net.Http.Headers;

namespace TouchdownAlert.Core.Sleeper;

/// <summary>
/// Asks Sleeper for gzip/brotli bodies and decodes them, as a <see cref="DelegatingHandler"/> rather than
/// <c>SocketsHttpHandler.AutomaticDecompression</c>. Reason: the integration tests route every HttpClient at an
/// in-memory TestServer with <c>ConfigureHttpClientDefaults(b =&gt; b.ConfigurePrimaryHttpMessageHandler(...))</c>,
/// and .NET applies those defaults *before* per-client configuration - so a per-client primary handler would
/// silently win over the tests' handler and hit the real network. An additional handler composes with any
/// primary handler, and passes bodies through untouched when the server didn't compress them.
/// </summary>
public sealed class SleeperDecompressionHandler : DelegatingHandler
{
    private static readonly string[] SupportedEncodings = { "gzip", "br" };

    public SleeperDecompressionHandler()
    {
    }

    public SleeperDecompressionHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers.AcceptEncoding.Count == 0)
        {
            foreach (var encoding in SupportedEncodings)
            {
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
            }
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var contentEncoding = response.Content.Headers.ContentEncoding;
        if (contentEncoding.Count != 1)
        {
            return response;
        }

        var raw = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        Stream? decoded = contentEncoding.Single().ToLowerInvariant() switch
        {
            "gzip" => new GZipStream(raw, CompressionMode.Decompress),
            "br" => new BrotliStream(raw, CompressionMode.Decompress),
            _ => null,
        };

        if (decoded is null)
        {
            return response;
        }

        var original = response.Content;
        var replacement = new StreamContent(decoded);
        foreach (var header in original.Headers)
        {
            // Length and encoding describe the compressed body and would be wrong for the decoded one.
            if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = replacement;
        return response;
    }
}
