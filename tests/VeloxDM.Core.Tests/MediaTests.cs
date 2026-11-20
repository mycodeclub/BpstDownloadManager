using VeloxDM.Core.Media;

namespace VeloxDM.Core.Tests;

public class MediaTests
{
    [Fact]
    public void Writes_netscape_cookie_lines_yt_dlp_can_read()
    {
        var text = CookieFile.ToNetscape(
        [
            new CookieEntry(".youtube.com", "/", "LOGIN_INFO", "abc", Secure: true, HttpOnly: true, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new CookieEntry("www.youtube.com", "/", "PREF", "f6=40", Secure: false, HttpOnly: false, null),
        ]);

        Assert.StartsWith("# Netscape HTTP Cookie File", text);
        Assert.Contains("#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t1893456000\tLOGIN_INFO\tabc\n", text);
        Assert.Contains("www.youtube.com\tFALSE\t/\tFALSE\t0\tPREF\tf6=40\n", text);
    }

    [Theory]
    [InlineData("[youtube] Y9VrScEzsb8: Sign in to confirm your age. This video may be inappropriate for some users.", true)]
    [InlineData("[youtube] x: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies", true)]
    [InlineData("Failed to decrypt with DPAPI. See https://github.com/yt-dlp/yt-dlp/issues/10927", true)]
    [InlineData("[youtube] x: Private video. Sign in if you've been granted access to this video", true)]
    [InlineData("[generic] Unable to download webpage: HTTP Error 404: Not Found", false)]
    public void Recognises_sign_in_errors(string ytDlpError, bool needsSignIn)
    {
        var ex = new MediaDownloader(new MediaTools(Path.GetTempPath())).Translate(ytDlpError);
        Assert.Equal(needsSignIn, ex is MediaSignInRequiredException);
    }
}
