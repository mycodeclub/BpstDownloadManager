namespace BpstVideoDownloader.Core;

/// <summary>Publisher details shown in the app, installer and About dialog. Change them here only.</summary>
public static class Branding
{
    public const string Product = "BpstVideoDownloader";
    public const string Company = "BitProSoftTech";
    public const string Website = "bitprosofttech.com";
    public const string WebsiteUrl = "https://bitprosofttech.com";

    public const string SourceUrl = "https://github.com/mycodeclub/BpstDownloadManager";
    public const string ProjectPageUrl = "https://mycodeclub.github.io/BpstDownloadManager/";
    public static readonly Uri SourceUri = new(SourceUrl);

    /// <summary>For XAML Hyperlink.NavigateUri, which needs a Uri (x:Static does not convert strings).</summary>
    public static readonly Uri WebsiteUri = new(WebsiteUrl);
    public const string Phone = "+(91)-82-9910-1616";

    public static string Version =>
        typeof(Branding).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
}
