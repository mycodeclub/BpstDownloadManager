using BpstVideoDownloader.Core.Http;

namespace BpstVideoDownloader.Core.Tests;

public class UrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", UrlKind.Media)]
    [InlineData("https://youtu.be/abc", UrlKind.Media)]
    [InlineData("https://cdn.example.com/live/master.m3u8?token=1", UrlKind.Media)]
    [InlineData("https://cdn.example.com/video/manifest.mpd", UrlKind.Media)]
    [InlineData("https://example.com/files/setup.exe", UrlKind.Direct)]
    [InlineData("https://example.com/movie.MP4", UrlKind.Direct)]
    [InlineData("https://example.com/some/page", UrlKind.Unknown)]
    [InlineData("blob:https://example.com/2b1f-44", UrlKind.Blob)]
    [InlineData("ftp://example.com/a.zip", UrlKind.Invalid)]
    [InlineData("hello world", UrlKind.Invalid)]
    public void Classifies_urls(string url, UrlKind expected) => Assert.Equal(expected, UrlClassifier.Classify(url));

    [Theory]
    [InlineData("  https://example.com/a.zip \r\n", "https://example.com/a.zip")]
    [InlineData("check https://example.com/a.zip", null)]
    [InlineData("\"https://example.com/a.zip\"", "https://example.com/a.zip")]
    public void Extracts_single_url_from_clipboard(string text, string? expected) => Assert.Equal(expected, UrlClassifier.ExtractUrl(text));

    [Theory]
    [InlineData("a<b>c?.mp4", "a_b_c_.mp4")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("name. . ", "name")]
    public void Sanitizes_file_names(string input, string expected) => Assert.Equal(expected, FileNameHelper.Sanitize(input));

    [Fact]
    public void Url_file_name_is_decoded()
        => Assert.Equal("my file.zip", FileNameHelper.FromUrl(new Uri("https://x.com/dl/my%20file.zip?x=1")));
}
