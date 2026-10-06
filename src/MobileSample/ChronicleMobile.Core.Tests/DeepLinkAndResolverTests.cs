using System.Net;
using System.Text;
using ChronicleMobile.Core;
using Xunit;

namespace ChronicleMobile.Core.Tests;

public sealed class DeepLinkAndResolverTests
{
    [Theory]
    [InlineData("ChronicleMobile://?shortid=hello%20world", "hello world")]
    [InlineData("chroniclemobile://?shortid=a%2Fb", "a/b")]
    [InlineData("https://short.gochronicle.com/m/hello%20world", "hello world")]
    [InlineData("HTTPS://SHORT.GOCHRONICLE.COM/m/a%2Fb", "a/b")]
    [InlineData("https://short.gochronicle.com/m/a%20b", "a b")]
    [InlineData("https://short.gochronicle.com/m/a%2Bb", "a+b")]
    [InlineData("https://short.gochronicle.com/m/a%252Fb", "a%2Fb")]
    public void TryParse_AcceptsCustomSchemeAndDecodesShortId(string uri, string expected)
    {
        Assert.True(DeepLinkParser.TryParse(uri, out var shortId));
        Assert.Equal(expected, shortId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://short.gochronicle.com/?shortid=id")]
    [InlineData("ChronicleMobile://host?shortid=id")]
    [InlineData("ChronicleMobile:///?shortid=id")]
    [InlineData("ChronicleMobile://?shortid=")]
    [InlineData("ChronicleMobile://?shortid=id&shortid=other")]
    [InlineData("ChronicleMobile://?shortid=id&extra=x")]
    [InlineData("ChronicleMobile://?extra=x&shortid=id")]
    [InlineData("ChronicleMobile://?shortid=id#fragment")]
    [InlineData("ChronicleMobile://?shortid=%ZZ")]
    [InlineData("http://short.gochronicle.com/m/id")]
    [InlineData("https://other.gochronicle.com/m/id")]
    [InlineData("https://short.gochronicle.com.evil.test/m/id")]
    [InlineData("https://evilshort.gochronicle.com/m/id")]
    [InlineData("https://short.gochronicle.com:444/m/id")]
    [InlineData("https://user@short.gochronicle.com/m/id")]
    [InlineData("https://short.gochronicle.com/m/a/b")]
    [InlineData("https://short.gochronicle.com/other/../m/id")]
    [InlineData("https://short.gochronicle.com/m/../id")]
    [InlineData("https://short.gochronicle.com/m/%2e%2e")]
    [InlineData("https://short.gochronicle.com/m/id?x=1")]
    [InlineData("https://short.gochronicle.com/m/id?")]
    [InlineData("https://short.gochronicle.com/m/id#fragment")]
    [InlineData("https://short.gochronicle.com/m/id#")]
    [InlineData("https://short.gochronicle.com/m/%ZZ")]
    [InlineData("https://short.gochronicle.com/m/")]
    [InlineData("https://short.gochronicle.com/m/%20%20")]
    public void TryParse_RejectsInvalidUris(string? uri)
    {
        Assert.False(DeepLinkParser.TryParse(uri, out _));
    }

    [Fact]
    public async Task ResolveAsync_InvalidUri_ReturnsVisibleStateWithoutRequest()
    {
        var handler = new RecordingHandler();
        var client = new ResolverClient(new HttpClient(handler));

        var result = await client.ResolveAsync("ChronicleMobile://?shortid=");

        Assert.Equal(ResolutionStatus.InvalidUri, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ResolveAsync_MissingShortId_ReturnsVisibleStateWithoutRequest()
    {
        var handler = new RecordingHandler();
        var client = new ResolverClient(new HttpClient(handler));

        var result = await client.ResolveAsync("ChronicleMobile://?other=id");

        Assert.Equal(ResolutionStatus.InvalidUri, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("ChronicleMobile://?shortid=id&extra=x")]
    [InlineData("ChronicleMobile://?shortid=id&shortid=other")]
    [InlineData("ChronicleMobile://?extra=x&shortid=id")]
    [InlineData("ChronicleMobile://?shortid=id#fragment")]
    [InlineData("ChronicleMobile://host?shortid=id")]
    [InlineData("ChronicleMobile:///?shortid=id")]
    public async Task ResolveAsync_InvalidShape_ReturnsVisibleStateWithoutRequest(string uri)
    {
        var handler = new RecordingHandler();
        var client = new ResolverClient(new HttpClient(handler));

        var result = await client.ResolveAsync(uri);

        Assert.Equal(ResolutionStatus.InvalidUri, result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("ChronicleMobile://?shortid=a%2Fb")]
    [InlineData("https://short.gochronicle.com/m/a%2Fb")]
    public async Task ResolveAsync_ValidUri_RequestsEncodedAnonymousUrlAndReturnsMetadata(string uri)
    {
        var handler = new RecordingHandler("{\"title\":\"Welcome\"}");
        var client = new ResolverClient(new HttpClient(handler));

        var result = await client.ResolveAsync(uri);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("a/b", result.ShortId);
        Assert.Equal("https://short.gochronicle.com/resolve/a%2Fb", handler.RequestUri!.AbsoluteUri);
        Assert.Null(handler.Authorization);
        Assert.Equal("Welcome", result.Metadata!["title"]);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7071/", "ChronicleMobile://?shortid=a%2Fb", "http://127.0.0.1:7071/resolve/a%2Fb")]
    [InlineData("http://127.0.0.1:7071", "ChronicleMobile://?shortid=a%2Fb", "http://127.0.0.1:7071/resolve/a%2Fb")]
    [InlineData("http://127.0.0.1:7071/", "https://short.gochronicle.com/m/a%2Fb", "http://127.0.0.1:7071/resolve/a%2Fb")]
    public async Task ResolveAsync_InjectedBase_ComposesEncodedResolverUrl(string resolverBase, string uri, string expectedUrl)
    {
        var handler = new RecordingHandler();
        var client = new ResolverClient(new HttpClient(handler), new Uri(resolverBase));

        await client.ResolveAsync(uri);

        Assert.Equal(expectedUrl, handler.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("ChronicleMobile://?shortid=id", HttpStatusCode.NotFound, ResolutionStatus.Missing)]
    [InlineData("https://short.gochronicle.com/m/id", HttpStatusCode.NotFound, ResolutionStatus.Missing)]
    [InlineData("ChronicleMobile://?shortid=id", HttpStatusCode.Gone, ResolutionStatus.Archived)]
    [InlineData("https://short.gochronicle.com/m/id", HttpStatusCode.Gone, ResolutionStatus.Archived)]
    [InlineData("ChronicleMobile://?shortid=id", HttpStatusCode.BadGateway, ResolutionStatus.Error)]
    [InlineData("https://short.gochronicle.com/m/id", HttpStatusCode.BadGateway, ResolutionStatus.Error)]
    public async Task ResolveAsync_StatusesReturnExpectedVisibleState(string uri, HttpStatusCode statusCode, ResolutionStatus expected)
    {
        var handler = new RecordingHandler("ignored", statusCode);
        var client = new ResolverClient(new HttpClient(handler));

        var result = await client.ResolveAsync(uri);

        Assert.Equal(expected, result.Status);
        Assert.Equal("id", result.ShortId);
    }

    [Theory]
    [InlineData("ChronicleMobile://?shortid=id")]
    [InlineData("https://short.gochronicle.com/m/id")]
    public async Task ResolveAsync_StatusAndMetadataAreConsistentAcrossFormats(string uri)
    {
        var handler = new RecordingHandler("{\"title\":\"Welcome\"}", HttpStatusCode.Gone);
        var client = new ResolverClient(new HttpClient(handler));

        var result = await client.ResolveAsync(uri);

        Assert.Equal(ResolutionStatus.Archived, result.Status);
        Assert.Equal("id", result.ShortId);
        Assert.Null(result.Metadata);
    }

    [Theory]
    [InlineData("{}", ResolutionStatus.Resolved)]
    [InlineData("[]", ResolutionStatus.Error)]
    [InlineData("{\"count\":1}", ResolutionStatus.Error)]
    [InlineData("not json", ResolutionStatus.Error)]
    public async Task ResolveAsync_OnlyAcceptsStringDictionaryJson(string body, ResolutionStatus expected)
    {
        var client = new ResolverClient(new HttpClient(new RecordingHandler(body)));

        var result = await client.ResolveAsync("ChronicleMobile://?shortid=id");

        Assert.Equal(expected, result.Status);
        if (expected == ResolutionStatus.Resolved)
        {
            Assert.Empty(result.Metadata!);
        }
        else
        {
            Assert.Null(result.Metadata);
        }
    }

    private sealed class RecordingHandler(string body = "{}", HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
