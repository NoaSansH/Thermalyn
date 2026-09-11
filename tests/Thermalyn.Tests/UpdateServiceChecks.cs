// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Thermalyn.Services;

internal static class UpdateServiceChecks
{
    public static async Task RunLiveAsync()
    {
        using var service = new UpdateService();
        var fromOldVersion = await service.CheckAsync(new Version(0, 0, 0), CancellationToken.None)
            ?? throw new InvalidOperationException("The live GitHub API returned no stable Thermalyn release.");
        Console.WriteLine($"Live GitHub Release: {fromOldVersion.Tag} · {fromOldVersion.InstallerSize} bytes · {fromOldVersion.InstallerUri}");

        var installed = typeof(Thermalyn.App).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var fromInstalledVersion = await service.CheckAsync(installed, CancellationToken.None);
        Console.WriteLine(fromInstalledVersion is null
            ? $"Installed {installed.ToString(3)}: no newer stable GitHub Release."
            : $"Installed {installed.ToString(3)}: update {fromInstalledVersion.Version.ToString(3)} detected.");
    }

    public static async Task RunAsync()
    {
        const string installerUrl = "https://github.com/NoaSansH/Thermalyn/releases/download/v9.8.7/Thermalyn-Setup.exe";
        const string checksumUrl = "https://github.com/NoaSansH/Thermalyn/releases/download/v9.8.7/SHA256SUMS.txt";
        var installer = Encoding.UTF8.GetBytes("a deterministic fake installer");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var release = $$"""
            {
              "tag_name": "v9.8.7",
              "name": "Thermalyn 9.8.7",
              "body": "Test release notes",
              "draft": false,
              "prerelease": false,
              "published_at": "2026-09-09T12:00:00Z",
              "assets": [
                { "name": "Thermalyn-Setup.exe", "size": {{installer.Length}}, "browser_download_url": "{{installerUrl}}" },
                { "name": "SHA256SUMS.txt", "size": 100, "browser_download_url": "{{checksumUrl}}" }
              ]
            }
            """;
        var root = Path.Combine(Path.GetTempPath(), "Thermalyn-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new FakeHandler(request => request.RequestUri?.AbsoluteUri switch
            {
                "https://api.github.com/repos/NoaSansH/Thermalyn/releases/latest" => Json(release),
                checksumUrl => Text($"{hash}  Thermalyn-Setup.exe\n"),
                installerUrl => Bytes(installer),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            }));
            using var service = new UpdateService(client, root);
            var available = await service.CheckAsync(new Version(1, 0, 4), CancellationToken.None)
                ?? throw new InvalidOperationException("A newer fake release was not detected.");
            var progress = new List<int>();
            var download = await service.DownloadAsync(available,
                new InlineProgress(value => progress.Add(value.Percentage)), CancellationToken.None);
            if (!File.ReadAllBytes(download.Path).SequenceEqual(installer) || progress.LastOrDefault() != 100)
                throw new InvalidOperationException("The verified update download did not complete correctly.");
            await using (var locked = await UpdateService.OpenVerifiedInstallerAsync(download, CancellationToken.None))
            {
                if (locked.Length != installer.Length)
                    throw new InvalidOperationException("The pre-install integrity check returned the wrong file.");
            }
            File.WriteAllText(download.Path, "tampered");
            try
            {
                await using var ignored = await UpdateService.OpenVerifiedInstallerAsync(download, CancellationToken.None);
                throw new InvalidOperationException("A modified installer passed the pre-install integrity check.");
            }
            catch (InvalidDataException) { }

            var badRelease = release.Replace("v9.8.7", "v1.0.3", StringComparison.Ordinal)
                .Replace("9.8.7", "1.0.3", StringComparison.Ordinal);
            using var oldClient = new HttpClient(new FakeHandler(_ => Json(badRelease)));
            using var oldService = new UpdateService(oldClient, root);
            if (await oldService.CheckAsync(new Version(1, 0, 4), CancellationToken.None) is not null)
                throw new InvalidOperationException("An older release was offered as an update.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        Console.WriteLine("Update API and verified background download checks passed.");
    }

    private static HttpResponseMessage Json(string value) => Response(value, "application/json");
    private static HttpResponseMessage Text(string value) => Response(value, "text/plain");
    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    private static HttpResponseMessage Response(string value, string mediaType) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, mediaType)
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class InlineProgress(Action<UpdateDownloadProgress> report) : IProgress<UpdateDownloadProgress>
    {
        public void Report(UpdateDownloadProgress value) => report(value);
    }
}
