using System.Net;
using System.Net.Http;
using LiteLauncher.Core;
using Xunit;

namespace LiteLauncher.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta", "1.2.3")]
    [InlineData("V2.0", "2.0")]
    public void Version_tags_are_parsed(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("nightly-2026")]
    public void Non_version_tags_are_ignored(string? tag) => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void Latest_release_json_is_read()
    {
        var info = UpdateChecker.ParseLatestRelease("""{"tag_name":"v1.4.0","html_url":"https://github.com/o/r/releases/tag/v1.4.0","draft":false,"prerelease":false}""");
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 4, 0), info!.Version);
        Assert.Equal("v1.4.0", info.Tag);
    }

    [Theory]
    [InlineData("""{"tag_name":"v1.4.0","html_url":"https://x/y","draft":true}""")]
    [InlineData("""{"tag_name":"v1.4.0","html_url":"https://x/y","prerelease":true}""")]
    [InlineData("""{"tag_name":"v1.4.0","html_url":"http://insecure/y"}""")]
    [InlineData("""{"tag_name":"v1.4.0","html_url":"https://evil.example/releases/v1.4.0"}""")]
    [InlineData("""{"tag_name":"v1.4.0"}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Drafts_prereleases_and_bad_json_are_ignored(string json) => Assert.Null(UpdateChecker.ParseLatestRelease(json));

    [Fact]
    public void Newer_is_compared_ignoring_the_revision_and_dev_builds_never_update()
    {
        Assert.True(UpdateChecker.IsNewer(new Version(1, 2, 0), new Version(1, 1, 9, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(1, 1, 0), new Version(1, 1, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(1, 0, 0), new Version(1, 1, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(9, 0, 0), new Version(0, 0, 0)));
    }

    [Fact]
    public async Task Check_without_a_repository_makes_no_request()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("no request expected")));
        Assert.Null(await UpdateChecker.CheckAsync(http, "", new Version(1, 0, 0)));
        Assert.Null(await UpdateChecker.CheckAsync(http, "justaname", new Version(1, 0, 0)));
    }

    [Fact]
    public async Task Check_returns_the_newer_release_and_null_when_up_to_date_or_offline()
    {
        const string json = """{"tag_name":"v2.0.0","html_url":"https://github.com/o/r/releases/tag/v2.0.0"}""";
        using var ok = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
        Assert.NotNull(await UpdateChecker.CheckAsync(ok, "o/r", new Version(1, 0, 0)));
        Assert.Null(await UpdateChecker.CheckAsync(ok, "o/r", new Version(2, 0, 0)));

        using var down = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline")));
        Assert.Null(await UpdateChecker.CheckAsync(down, "o/r", new Version(1, 0, 0)));

        using var limited = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Assert.Null(await UpdateChecker.CheckAsync(limited, "o/r", new Version(1, 0, 0)));
    }
}

public sealed class CoverDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ll-covers-" + Guid.NewGuid().ToString("N"));
    private static readonly Game Steam = new("Steam", "570", "Dota 2");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static HttpResponseMessage Image(int size = 2000) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[size]) { Headers = { ContentType = new("image/jpeg") } } };

    [Fact]
    public async Task Downloads_once_and_reuses_the_cache()
    {
        var calls = 0;
        using var http = new HttpClient(new FakeHandler(_ => { calls++; return Image(); }));

        var first = await CoverDownloader.DownloadAsync(http, _dir, Steam, StoreArtKind.Cover);
        var second = await CoverDownloader.DownloadAsync(http, _dir, Steam, StoreArtKind.Cover);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(1, calls);
        Assert.Equal(first, CoverDownloader.Cached(_dir, Steam, StoreArtKind.Cover));
        Assert.Null(CoverDownloader.Cached(_dir, Steam, StoreArtKind.Backdrop));
    }

    [Fact]
    public async Task Falls_back_to_the_next_url_when_the_first_is_missing()
    {
        var requested = new List<string>();
        using var http = new HttpClient(new FakeHandler(r =>
        {
            requested.Add(r.RequestUri!.AbsolutePath);
            return r.RequestUri.AbsolutePath.EndsWith("library_600x900.jpg") ? new HttpResponseMessage(HttpStatusCode.NotFound) : Image();
        }));

        Assert.NotNull(await CoverDownloader.DownloadAsync(http, _dir, Steam, StoreArtKind.Cover));
        Assert.Equal(2, requested.Count);
        Assert.EndsWith("header.jpg", requested[1]);
    }

    [Fact]
    public async Task Html_error_pages_and_tiny_bodies_are_not_saved()
    {
        using var html = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html/>") }));
        Assert.Null(await CoverDownloader.DownloadAsync(html, _dir, Steam, StoreArtKind.Cover));

        using var tiny = new HttpClient(new FakeHandler(_ => Image(size: 10)));
        Assert.Null(await CoverDownloader.DownloadAsync(tiny, _dir, Steam, StoreArtKind.Cover));
        Assert.Null(CoverDownloader.Cached(_dir, Steam, StoreArtKind.Cover));
    }

    [Fact]
    public async Task Only_steam_apps_with_numeric_ids_are_fetched()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("no request expected")));
        Assert.Null(await CoverDownloader.DownloadAsync(http, _dir, new Game("Epic", "abc", "X"), StoreArtKind.Cover));
        Assert.Null(await CoverDownloader.DownloadAsync(http, _dir, new Game("Steam", "../evil", "X"), StoreArtKind.Cover));
    }

    [Fact]
    public async Task Offline_returns_null_instead_of_throwing()
    {
        using var http = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline")));
        Assert.Null(await CoverDownloader.DownloadAsync(http, _dir, Steam, StoreArtKind.Cover));
    }
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_respond(request));
}
