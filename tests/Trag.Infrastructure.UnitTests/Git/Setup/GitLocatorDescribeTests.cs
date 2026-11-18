using Trag.Domain.Setup;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Setup;

namespace Trag.Infrastructure.UnitTests.Git.Setup;

// A configuration larger than any Git output Trag takes, as a global one a tool kept appending to, describes nothing
// rather than failing the Settings page that asked; here the "Git" prints such an answer itself.
public sealed class GitLocatorDescribeTests
{
    [Fact]
    public async Task WhenTheConfigurationIsLargerThanTheOutputLimit_ShouldDescribeNothing()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "trag-big-config-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var answer = Path.Combine(folder, "config.txt");
            await File.WriteAllTextAsync(answer, new string('x', 9 * 1024 * 1024));
            var git = Path.Combine(folder, "git.cmd");
            await File.WriteAllTextAsync(git, "@type \"" + answer + "\"\r\n");

            var features = await new GitLocator(new GitExecutable()).DescribeAsync(new(git, new(2, 50), GitOrigin.Chosen), CancellationToken.None);

            Assert.Null(features);
        }
        finally { Directory.Delete(folder, true); }
    }
}
