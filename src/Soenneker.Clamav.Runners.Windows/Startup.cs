using Microsoft.Extensions.DependencyInjection;
using Soenneker.Clamav.Runners.Windows.Utils;
using Soenneker.Clamav.Runners.Windows.Utils.Abstract;
using Soenneker.GitHub.Repositories.Releases.Registrars;
using Soenneker.Managers.Runners.Registrars;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.File.Registrars;

namespace Soenneker.Clamav.Runners.Windows;

public static class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
                .AddDirectoryUtilAsSingleton()
                .AddFileUtilAsSingleton()
                .AddGitHubRepositoriesReleasesUtilAsSingleton()
                .AddRunnersManagerAsSingleton();
    }
}
