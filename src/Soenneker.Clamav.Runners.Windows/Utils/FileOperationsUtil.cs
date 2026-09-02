using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Clamav.Runners.Windows.Utils.Abstract;
using Soenneker.GitHub.Repositories.Releases.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Clamav.Runners.Windows.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private const string Owner = "Cisco-Talos";
    private const string Repository = "clamav";
    private static readonly string[] _assetPatterns = [".win.x64.zip"];

    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IGitHubRepositoriesReleasesUtil _releasesUtil;
    private readonly IFileUtil _fileUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IDirectoryUtil directoryUtil,
        IGitHubRepositoriesReleasesUtil releasesUtil, IFileUtil fileUtil)
    {
        _logger = logger;
        _directoryUtil = directoryUtil;
        _releasesUtil = releasesUtil;
        _fileUtil = fileUtil;
    }

    public async ValueTask<string> Process(CancellationToken cancellationToken = default)
    {
        string downloadDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken);
        string? asset = await _releasesUtil.DownloadReleaseAssetByNamePattern(Owner, Repository, downloadDirectory,
            _assetPatterns, cancellationToken);

        if (asset is null)
            throw new FileNotFoundException("Could not find the Windows x64 ZIP in the latest stable ClamAV release.");

        string extractDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken);
        ZipFile.ExtractToDirectory(asset, extractDirectory);

        string[] scanners = Directory.GetFiles(extractDirectory, "clamscan.exe", SearchOption.AllDirectories);
        if (scanners.Length != 1)
            throw new FileNotFoundException("The ClamAV archive did not contain exactly one clamscan.exe executable.");

        string stageDirectory = Path.GetDirectoryName(scanners[0])!;
        string freshclamPath = Path.Combine(stageDirectory, "freshclam.exe");
        if (!await _fileUtil.Exists(freshclamPath, cancellationToken))
            throw new FileNotFoundException("The ClamAV archive did not contain freshclam.exe.", freshclamPath);

        RemoveDevelopmentFiles(stageDirectory);

        await _fileUtil.Write(Path.Combine(stageDirectory, "SOURCE.txt"),
            $"Official release archive from https://github.com/{Owner}/{Repository}/releases/latest{Environment.NewLine}Asset: {Path.GetFileName(asset)}{Environment.NewLine}",
            log: false, cancellationToken);

        _logger.LogInformation("Prepared Windows x64 ClamAV runtime at {StageDirectory}", stageDirectory);
        return stageDirectory;
    }

    private static void RemoveDevelopmentFiles(string stageDirectory)
    {
        foreach (string pattern in new[] {"*.pdb", "*.lib"})
        {
            foreach (string file in Directory.EnumerateFiles(stageDirectory, pattern, SearchOption.AllDirectories))
                File.Delete(file);
        }

        foreach (string name in new[] {"include", "UserManual"})
        {
            string directory = Path.Combine(stageDirectory, name);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
