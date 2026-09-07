using System.IO.Compression;
using System.Net;
using System.Text;
using TouchdownAlert.Core.Sleeper;
using static TouchdownAlert.Core.Tests.SleeperTestSupport;

namespace TouchdownAlert.Core.Tests;

public class SleeperDecompressionHandlerTests
{
    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static HttpClient Client(RoutingHandler inner) =>
        new(new SleeperDecompressionHandler(inner)) { BaseAddress = new Uri("https://api.sleeper.app/") };

    [Fact]
    public async Task GzipBody_IsDecoded_AndAcceptEncodingSent()
    {
        string? acceptEncoding = null;
        var inner = new RoutingHandler(request =>
        {
            acceptEncoding = request.Headers.AcceptEncoding.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip("""{"week":3}""")) };
            response.Content.Headers.ContentEncoding.Add("gzip");
            response.Content.Headers.ContentType = new("application/json");
            return response;
        });

        var body = await Client(inner).GetStringAsync("v1/state/nfl");

        Assert.Equal("""{"week":3}""", body);
        Assert.Contains("gzip", acceptEncoding);
        Assert.Contains("br", acceptEncoding);
    }

    [Fact]
    public async Task UncompressedBody_PassesThrough()
    {
        var inner = new RoutingHandler(_ => JsonOk("""{"week":3}"""));

        using var response = await Client(inner).GetAsync("v1/state/nfl");

        Assert.Equal("""{"week":3}""", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task UnknownEncoding_LeftForTheCaller()
    {
        var inner = new RoutingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            response.Content.Headers.ContentEncoding.Add("zstd");
            return response;
        });

        using var response = await Client(inner).GetAsync("v1/state/nfl");

        Assert.Equal("zstd", response.Content.Headers.ContentEncoding.Single());
        Assert.Equal(3, (await response.Content.ReadAsByteArrayAsync()).Length);
    }
}
