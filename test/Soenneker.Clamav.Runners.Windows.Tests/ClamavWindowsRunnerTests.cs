using Soenneker.Tests.HostedUnit;

namespace Soenneker.Clamav.Runners.Windows.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class ClamavWindowsRunnerTests : HostedUnitTest
{
    public ClamavWindowsRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {
    }
}
