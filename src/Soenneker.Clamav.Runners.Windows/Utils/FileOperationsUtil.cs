using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Clamav.Runners.Windows.Utils.Abstract;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
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
        string downloadDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
        string? asset = await _releasesUtil.DownloadReleaseAssetByNamePattern(Owner, Repository, downloadDirectory,
            _assetPatterns, cancellationToken).NoSync();

        if (asset is null)
            throw new FileNotFoundException("Could not find the Windows x64 ZIP in the latest stable ClamAV release.");

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
            $"Official release archive from https://github.com/{Owner}/{Repository}/releases/latest{Environment.NewLine}Asset: {Path.GetFileName(asset)}{Environment.NewLine}",
            log: false, cancellationToken).NoSync();

        _logger.LogInformation("Prepared Windows x64 ClamAV runtime at {StageDirectory}", stageDirectory);
        return stageDirectory;
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
