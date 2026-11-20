using BpstVideoDownloader.Core.Media;

namespace BpstVideoDownloader.Core.Tests;

public class StreamDetectorTests
{
    [Theory]
    [InlineData("https://cdn.example.com/hls/master.m3u8?token=x", null, null, DetectedStreamKind.Hls)]
    [InlineData("https://cdn.example.com/play/abc", "application/vnd.apple.mpegurl", null, DetectedStreamKind.Hls)]
    [InlineData("https://cdn.example.com/v/manifest.mpd", null, null, DetectedStreamKind.Dash)]
    [InlineData("https://cdn.example.com/v/file.mp4", "video/mp4", 50_000_000L, DetectedStreamKind.File)]
    [InlineData("https://cdn.example.com/get?id=9", "video/webm", 3_000_000L, DetectedStreamKind.File)]
    public void Detects_streams(string url, string? contentType, long? size, DetectedStreamKind expected)
        => Assert.Equal(expected, StreamDetector.Classify(url, contentType, size)?.Kind);

    [Theory]
    [InlineData("https://cdn.example.com/hls/seg-12-v1-a1.ts", "video/mp2t", 900_000L)]
    [InlineData("https://cdn.example.com/dash/chunk-stream0-00012.m4s", "video/iso.segment", 700_000L)]
    [InlineData("https://cdn.example.com/thumbs/preview.mp4", "video/mp4", 80_000L)]
    [InlineData("https://example.com/page", "text/html", 40_000L)]
    [InlineData("blob:https://example.com/d456f412", null, null)]
    public void Ignores_segments_previews_and_pages(string url, string? contentType, long? size)
        => Assert.Null(StreamDetector.Classify(url, contentType, size));

    [Fact]
    public void Reads_total_from_content_range() => Assert.Equal(52428800L, StreamDetector.TotalFromContentRange("bytes 0-1023/52428800"));
}
