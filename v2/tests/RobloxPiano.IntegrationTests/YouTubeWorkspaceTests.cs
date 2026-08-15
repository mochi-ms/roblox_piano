using RobloxPiano.Infrastructure.YouTube;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class YouTubeWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly YouTubeWorkspaceService _workspace;

    public YouTubeWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "YTWorkspaceTests_" + Guid.NewGuid().ToString("N"));
        _workspace = new YouTubeWorkspaceService(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public void YouTubeWorkspace_ValidJobId_Works()
    {
        string jobId = "job_valid_12345";
        string jobDir = _workspace.GetJobDirectory(jobId);
        string expectedWav = _workspace.GetSourceWavPath(jobId);
        string outputTemplate = _workspace.GetOutputTemplate(jobId);

        Assert.True(Directory.Exists(jobDir));
        Assert.StartsWith(_tempRoot, jobDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("source.wav", expectedWav, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("source.%(ext)s", outputTemplate, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("../../etc")]
    [InlineData("job/with/slashes")]
    [InlineData("job\\with\\backslashes")]
    [InlineData(" ")]
    [InlineData("")]
    public void YouTubeWorkspace_TraversalRejected(string invalidJobId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            _workspace.GetJobDirectory(invalidJobId);
        });
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"D:\Data")]
    public void YouTubeWorkspace_RootedPathRejected(string rootedJobId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            _workspace.GetJobDirectory(rootedJobId);
        });
    }

    [Theory]
    [InlineData(@"\\server\share\job")]
    [InlineData(@"\\127.0.0.1\c$\job")]
    public void YouTubeWorkspace_UncRejected(string uncJobId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            _workspace.GetJobDirectory(uncJobId);
        });
    }

    [Fact]
    public void YouTubeWorkspace_CleanInvalidId_CannotDeleteOutsideRoot()
    {
        // Sentinel outside root
        string outsideSentinel = Path.Combine(Path.GetTempPath(), "sentinel_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outsideSentinel, "do-not-delete");

        try
        {
            Assert.ThrowsAny<ArgumentException>(() =>
            {
                _workspace.CleanJob("../" + Path.GetFileName(outsideSentinel));
            });

            Assert.True(File.Exists(outsideSentinel));
        }
        finally
        {
            if (File.Exists(outsideSentinel)) File.Delete(outsideSentinel);
        }
    }
}
