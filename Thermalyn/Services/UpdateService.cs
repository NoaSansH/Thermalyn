using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermalyn.Services;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Name,
    string ReleaseNotes,
    DateTimeOffset PublishedAt,
    Uri InstallerUri,
    long InstallerSize,
    Uri ChecksumsUri);

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int Percentage => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100)
        : 0;
}

public sealed record VerifiedUpdateDownload(string Path, string Sha256, long Size);

public sealed class UpdateService : IDisposable
{
    internal const string Repository = "NoaSansH/Thermalyn";
    internal const string InstallerAssetName = "Thermalyn-Setup.exe";
    internal const string ChecksumsAssetName = "SHA256SUMS.txt";
    private const long MaximumInstallerSize = 512L * 1024 * 1024;
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _updateRoot;

    public UpdateService(HttpClient? httpClient = null, string? updateRoot = null)
    {
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        _ownsClient = httpClient is null;
        _updateRoot = updateRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thermalyn", "Updates");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Thermalyn-Updater/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<UpdateInfo?> CheckAsync(Version installedVersion, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");

        if (release.Draft || release.Prerelease)
            throw new InvalidDataException("GitHub returned a release that is not stable.");
        if (!TryParseVersion(release.TagName, out var availableVersion))
            throw new InvalidDataException($"The release tag '{release.TagName}' is not a valid vX.Y.Z version.");
        if (availableVersion <= Normalize(installedVersion)) return null;

        var installer = FindTrustedAsset(release.Assets, InstallerAssetName);
        var checksums = FindTrustedAsset(release.Assets, ChecksumsAssetName);
        if (installer.Size is <= 0 or > MaximumInstallerSize)
            throw new InvalidDataException("The update installer has an invalid size.");

        return new UpdateInfo(availableVersion, release.TagName, release.Name, release.Body,
            release.PublishedAt, installer.BrowserDownloadUrl, installer.Size, checksums.BrowserDownloadUrl);
    }

    public async Task<VerifiedUpdateDownload> DownloadAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var expectedHash = await DownloadExpectedHashAsync(update.ChecksumsUri, cancellationToken);
        var updateFolder = Path.Combine(_updateRoot, update.Version.ToString(3));
        Directory.CreateDirectory(updateFolder);
        var destination = Path.Combine(updateFolder, InstallerAssetName);
        var temporary = destination + ".download";

        try
        {
            using var response = await _httpClient.GetAsync(update.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseSize = response.Content.Headers.ContentLength;
            if (responseSize is > MaximumInstallerSize || responseSize is > 0 && responseSize != update.InstallerSize)
                throw new InvalidDataException("The downloaded installer size does not match the GitHub Release.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                received += count;
                if (received > MaximumInstallerSize || received > update.InstallerSize)
                    throw new InvalidDataException("The downloaded installer is larger than the GitHub Release asset.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                hasher.AppendData(buffer, 0, count);
                progress?.Report(new UpdateDownloadProgress(received, update.InstallerSize));
            }
            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();

            if (received != update.InstallerSize)
                throw new InvalidDataException("The downloaded installer is incomplete.");
            var actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
                throw new InvalidDataException("SHA-256 verification failed. The update was not installed.");

            File.Move(temporary, destination, true);
            return new VerifiedUpdateDownload(destination, expectedHash, received);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    public static async Task<FileStream> OpenVerifiedInstallerAsync(
        VerifiedUpdateDownload download,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(download.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        try
        {
            if (stream.Length != download.Size)
                throw new InvalidDataException("The installer changed after it was downloaded.");
            var actual = await SHA256.HashDataAsync(stream, cancellationToken);
            var expected = Convert.FromHexString(download.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                throw new InvalidDataException("The installer changed after it was downloaded. Installation was cancelled.");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task<string> DownloadExpectedHashAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1024 * 1024)
            throw new InvalidDataException("The checksum file is unexpectedly large.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 1024 * 1024)
            throw new InvalidDataException("The checksum file is unexpectedly large.");
        return ParseExpectedHash(text, InstallerAssetName);
    }

    internal static bool TryParseVersion(string? tag, out Version version)
    {
        var value = tag?.Trim();
        if (value?.StartsWith("v", StringComparison.OrdinalIgnoreCase) == true) value = value[1..];
        if (Version.TryParse(value, out var parsed) && parsed.Major >= 0 && parsed.Minor >= 0 && parsed.Build >= 0)
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version();
        return false;
    }

    internal static string ParseExpectedHash(string contents, string assetName)
    {
        foreach (var line in contents.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !string.Equals(parts[^1].TrimStart('*'), assetName, StringComparison.Ordinal)) continue;
            var hash = parts[0].ToLowerInvariant();
            if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash;
        }
        throw new InvalidDataException($"{assetName} has no valid SHA-256 entry in {ChecksumsAssetName}.");
    }

    private static GitHubAsset FindTrustedAsset(IEnumerable<GitHubAsset> assets, string name)
    {
        var asset = assets.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The GitHub Release does not contain {name}.");
        if (!IsTrustedReleaseAssetUri(asset.BrowserDownloadUrl, name))
            throw new InvalidDataException($"GitHub returned an untrusted download URL for {name}.");
        return asset;
    }

    internal static bool IsTrustedReleaseAssetUri(Uri uri, string assetName) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal) &&
        string.Equals(Uri.UnescapeDataString(uri.Segments[^1]), assetName, StringComparison.Ordinal);

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
        [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] Uri BrowserDownloadUrl);
}
