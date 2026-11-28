using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Clamav.Runners.Windows.Utils.Abstract;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.GitHub.Client.Http.Abstract;
using Soenneker.GitHub.ClientUtil.Abstract;
using Soenneker.GitHub.OpenApiClient.Models;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Clamav.Runners.Windows.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    public string? Version { get; private set; }


    private const string Owner = "Cisco-Talos";
    private const string Repository = "clamav";

    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IGitHubOpenApiClientUtil _gitHubClient;
    private readonly IGitHubHttpClient _gitHubHttpClient;
    private readonly IFileUtil _fileUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IDirectoryUtil directoryUtil,
        IGitHubOpenApiClientUtil gitHubClient, IGitHubHttpClient gitHubHttpClient, IFileUtil fileUtil)
    {
        _logger = logger;
        _directoryUtil = directoryUtil;
        _gitHubClient = gitHubClient;
        _gitHubHttpClient = gitHubHttpClient;
        _fileUtil = fileUtil;
    }

    public async ValueTask<string> Process(CancellationToken cancellationToken = default)
    {
        var client = await _gitHubClient.Get(cancellationToken).NoSync();
        Release? release = await client.Repos[Owner][Repository].Releases.Latest.GetAsync(cancellationToken: cancellationToken).NoSync();
        if (release?.TagName is not string releaseTag || !releaseTag.StartsWith("clamav-", StringComparison.Ordinal))
            throw new InvalidDataException("The latest stable ClamAV release has no recognized release tag.");

        string assetName = $"{releaseTag}.win.x64.zip";
        ReleaseAsset? archive = release.Assets?.FirstOrDefault(asset => string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (archive?.BrowserDownloadUrl is not string downloadUrl)
            throw new FileNotFoundException($"Could not find '{assetName}' in the latest stable ClamAV release.");

        string downloadDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
        string asset = Path.Combine(downloadDirectory, "clamav.zip");
        HttpClient httpClient = await _gitHubHttpClient.Get(cancellationToken).NoSync();
        _logger.LogInformation("Downloading asset {AssetName} from {DownloadUrl}", assetName, downloadUrl);
        using (Stream source = await httpClient.GetStreamAsync(downloadUrl, cancellationToken).NoSync())
        await using (FileStream destination = File.Create(asset))
        {
            await source.CopyToAsync(destination, cancellationToken).NoSync();
        }

        string extractDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
        ZipFile.ExtractToDirectory(asset, extractDirectory);

        string[] files = await _fileUtil.GetAllFileNamesInDirectoryRecursively(extractDirectory, log: false, cancellationToken).NoSync();
        string[] scanners = files.Where(static file => Path.GetFileName(file).Equals("clamscan.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (scanners.Length != 1)
            throw new FileNotFoundException("The ClamAV archive did not contain exactly one clamscan.exe executable.");

        string stageDirectory = Path.GetDirectoryName(scanners[0])!;
        string freshclamPath = Path.Combine(stageDirectory, "freshclam.exe");
        if (!await _fileUtil.Exists(freshclamPath, cancellationToken).NoSync())
            throw new FileNotFoundException("The ClamAV archive did not contain freshclam.exe.", freshclamPath);

        await RemoveDevelopmentFiles(stageDirectory, cancellationToken).NoSync();

        await _fileUtil.Write(Path.Combine(stageDirectory, "SOURCE.txt"),
            $"Upstream project: https://github.com/{Owner}/{Repository}{Environment.NewLine}" +
            $"Release: https://github.com/{Owner}/{Repository}/releases/tag/{releaseTag}{Environment.NewLine}" +
            $"Binary asset: {assetName}{Environment.NewLine}" +
            $"Corresponding source: https://github.com/{Owner}/{Repository}/releases/download/{releaseTag}/{releaseTag}.tar.gz{Environment.NewLine}" +
            $"License: GPL-2.0-only; see COPYING.txt and COPYING/ in this directory.{Environment.NewLine}",
            log: false, cancellationToken).NoSync();

        _logger.LogInformation("Prepared Windows x64 ClamAV runtime at {StageDirectory}", stageDirectory);
        Version = releaseTag.Replace("clamav-", "");
        return stageDirectory;
    }

    public async ValueTask PrepareFreshclamRuntime(string stageDirectory, CancellationToken cancellationToken = default)
    {
        string[] files = Directory.GetFiles(stageDirectory);

        foreach (string file in files)
        {
            if (Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(file).Equals("freshclam.exe", StringComparison.OrdinalIgnoreCase))
                await _fileUtil.Delete(file, log: false, cancellationToken: cancellationToken).NoSync();
        }

        _logger.LogInformation("Reduced Windows runtime to FreshClam and supporting files at {StageDirectory}", stageDirectory);
    }

    private async ValueTask RemoveDevelopmentFiles(string stageDirectory, CancellationToken cancellationToken)
    {
        string[] files = await _fileUtil.GetAllFileNamesInDirectoryRecursively(stageDirectory, log: false, cancellationToken).NoSync();

        foreach (string file in files)
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) || extension.Equals(".lib", StringComparison.OrdinalIgnoreCase))
                await _fileUtil.Delete(file, log: false, cancellationToken: cancellationToken).NoSync();
        }

        foreach (string name in new[] { "include", "UserManual" })
        {
            string directory = Path.Combine(stageDirectory, name);
            await _directoryUtil.DeleteIfExists(directory, cancellationToken).NoSync();
        }
    }
}
