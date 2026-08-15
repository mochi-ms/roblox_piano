using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class AudioWorkspaceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workspaceDir;
    private readonly string _outsideSentinelDir;
    private readonly string _outsideSentinelFile;

    public AudioWorkspaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_workspace_tests_{Guid.NewGuid():N}");
        _workspaceDir = Path.Combine(_tempDir, "AudioWorkspace");
        _outsideSentinelDir = Path.Combine(_tempDir, "OutsideProtected");
        _outsideSentinelFile = Path.Combine(_outsideSentinelDir, "critical_sentinel.txt");

        Directory.CreateDirectory(_workspaceDir);
        Directory.CreateDirectory(_outsideSentinelDir);
        File.WriteAllText(_outsideSentinelFile, "SENTINEL_DO_NOT_DELETE");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData("c2e5661b36bb489fae0daecbca1dbeea")]
    [InlineData("c2e5661b-36bb-489f-ae0d-aecbca1dbeea")]
    [InlineData("job_123")]
    [InlineData("JOB-TEST-456")]
    public void AudioWorkspace_ValidGuidLikeJobId_Works(string validJobId)
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        Assert.True(AudioWorkspaceService.IsValidJobId(validJobId));

        string jobDir = workspace.GetJobDirectory(validJobId);
        string tempPath = workspace.GetTempNormalizedPath(validJobId);
        string finalPath = workspace.GetFinalNormalizedPath(validJobId);

        Assert.True(Directory.Exists(jobDir));
        Assert.StartsWith(_workspaceDir, jobDir, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(jobDir, tempPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(jobDir, finalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("../../outside")]
    [InlineData("..")]
    [InlineData(".")]
    public void AudioWorkspace_RejectsDotDotTraversal(string traversalId)
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        Assert.False(AudioWorkspaceService.IsValidJobId(traversalId));
        Assert.Throws<ArgumentException>(() => workspace.GetJobDirectory(traversalId));
        Assert.Throws<ArgumentException>(() => workspace.GetTempNormalizedPath(traversalId));
        Assert.Throws<ArgumentException>(() => workspace.GetFinalNormalizedPath(traversalId));
    }

    [Theory]
    [InlineData("job/subfolder")]
    [InlineData("/var/tmp")]
    public void AudioWorkspace_RejectsForwardSlashTraversal(string slashId)
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        Assert.False(AudioWorkspaceService.IsValidJobId(slashId));
        Assert.Throws<ArgumentException>(() => workspace.GetJobDirectory(slashId));
    }

    [Theory]
    [InlineData(@"job\subfolder")]
    [InlineData(@"..\outside")]
    public void AudioWorkspace_RejectsBackslashTraversal(string backslashId)
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        Assert.False(AudioWorkspaceService.IsValidJobId(backslashId));
        Assert.Throws<ArgumentException>(() => workspace.GetJobDirectory(backslashId));
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"D:\Data")]
    public void AudioWorkspace_RejectsRootedDrivePath(string rootedPath)
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        Assert.False(AudioWorkspaceService.IsValidJobId(rootedPath));
        Assert.Throws<ArgumentException>(() => workspace.GetJobDirectory(rootedPath));
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\127.0.0.1\c$")]
    public void AudioWorkspace_RejectsUncPath(string uncPath)
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        Assert.False(AudioWorkspaceService.IsValidJobId(uncPath));
        Assert.Throws<ArgumentException>(() => workspace.GetJobDirectory(uncPath));
    }

    [Fact]
    public void AudioWorkspace_CleanJob_InvalidId_DoesNotDeleteOutsideRoot()
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);

        // Try cleaning using malicious traversal IDs targeting outside sentinel directory
        workspace.CleanJob(@"..\OutsideProtected");
        workspace.CleanJob(@"../OutsideProtected");
        workspace.CleanJob(_outsideSentinelDir);
        workspace.CleanJob(null);
        workspace.CleanJob("");
        workspace.CleanJob("..");
        workspace.CleanJob(".");

        // Outside sentinel must remain completely untouched!
        Assert.True(Directory.Exists(_outsideSentinelDir));
        Assert.True(File.Exists(_outsideSentinelFile));
        Assert.Equal("SENTINEL_DO_NOT_DELETE", File.ReadAllText(_outsideSentinelFile));
    }

    [Fact]
    public void AudioWorkspace_CleanJob_ValidJobId_DeletesTargetDirectory()
    {
        var workspace = new AudioWorkspaceService(_workspaceDir);
        string jobId = "job_to_delete_01";
        string jobDir = workspace.GetJobDirectory(jobId);
        string tempFile = workspace.GetTempNormalizedPath(jobId);
        File.WriteAllText(tempFile, "temp data");

        Assert.True(Directory.Exists(jobDir));
        Assert.True(File.Exists(tempFile));

        workspace.CleanJob(jobId);

        Assert.False(Directory.Exists(jobDir));
    }
}
