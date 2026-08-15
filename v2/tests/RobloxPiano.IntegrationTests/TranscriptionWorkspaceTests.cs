using System.IO;
using RobloxPiano.Infrastructure.Transcription;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class TranscriptionWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TranscriptionWorkspaceService _workspace;

    public TranscriptionWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rp_transcribe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _workspace = new TranscriptionWorkspaceService(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData("c2e5661b36bb489fae0daecbca1dbeea")]
    [InlineData("c2e5661b-36bb-489f-ae0d-aecbca1dbeea")]
    [InlineData("job_123")]
    [InlineData("JOB-456_TEST")]
    public void IsValidJobId_AcceptsSafeIds(string jobId)
    {
        Assert.True(TranscriptionWorkspaceService.IsValidJobId(jobId));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("../outside")]
    [InlineData("job/subfolder")]
    [InlineData(@"job\subfolder")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"\\server\share")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("job with space")]
    [InlineData("job*bad")]
    public void IsValidJobId_RejectsTraversalAndSpecialChars(string? jobId)
    {
        Assert.False(TranscriptionWorkspaceService.IsValidJobId(jobId));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("../outside")]
    [InlineData("job/sub")]
    [InlineData(@"job\sub")]
    public void GetSafeJobDirectoryPath_RejectsPathTraversal(string jobId)
    {
        Assert.Throws<ArgumentException>(() => _workspace.GetSafeJobDirectoryPath(jobId));
    }

    [Fact]
    public void CommitMidiFile_AtomicallyMovesTempToFinal()
    {
        string jobId = "job_commit_test";
        string tempMidi = _workspace.GetTempMidiPath(jobId);
        string finalMidi = _workspace.GetFinalMidiPath(jobId);

        File.WriteAllBytes(tempMidi, new byte[] { 0x4D, 0x54, 0x68, 0x64 });

        string committed = _workspace.CommitMidiFile(jobId);

        Assert.Equal(finalMidi, committed);
        Assert.True(File.Exists(finalMidi));
        Assert.False(File.Exists(tempMidi));
    }

    [Fact]
    public void CleanJob_SafelyDeletesJobDirectory()
    {
        string jobId = "job_clean_test";
        string jobDir = _workspace.GetJobDirectory(jobId);
        string tempMidi = _workspace.GetTempMidiPath(jobId);
        File.WriteAllBytes(tempMidi, new byte[] { 1, 2, 3 });

        Assert.True(Directory.Exists(jobDir));

        _workspace.CleanJob(jobId);

        Assert.False(Directory.Exists(jobDir));
        Assert.True(Directory.Exists(_tempRoot));
    }

    [Fact]
    public void CleanJob_OutsideRoot_NeverDeletes()
    {
        string sentinelDir = Path.Combine(Path.GetTempPath(), "rp_outside_sentinel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sentinelDir);

        try
        {
            _workspace.CleanJob("../../rp_outside_sentinel");
            Assert.True(Directory.Exists(sentinelDir));
        }
        finally
        {
            if (Directory.Exists(sentinelDir))
            {
                Directory.Delete(sentinelDir, recursive: true);
            }
        }
    }
}
