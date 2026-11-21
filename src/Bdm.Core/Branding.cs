namespace Bdm.Core;

/// <summary>Publisher details shown in the app, installer and About page. Change them here only.</summary>
public static class Branding
{
    public const string Product = "BPST Download Manager";
    public const string ShortName = "BDM";
    public const string Company = "BitProSoftTech";
    public const string Website = "bitprosofttech.com";
    public const string WebsiteUrl = "https://bitprosofttech.com";
    public const string Phone = "+(91)-82-9910-1616";
    public const string SourceUrl = "https://github.com/mycodeclub/BpstDownloadManager";
    public const string ProjectPageUrl = "https://mycodeclub.github.io/BpstDownloadManager/";

    /// <summary>Folder name for app data (settings, download list, tools, logs).</summary>
    public const string DataFolderName = "BDM";

    public static string Version =>
        typeof(Branding).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
}
