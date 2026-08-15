using RobloxPiano.Core.Music;
using RobloxPiano.Core.Transcription;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class TranscriptionDomainTests
{
    [Fact]
    public void TranscriptionOptions_Default_UsesOfficialBasicPitchValues()
    {
        var opts = TranscriptionOptions.Default;

        Assert.Equal(0.5, opts.OnsetThreshold);
        Assert.Equal(0.3, opts.FrameThreshold);
        Assert.Equal(127.7, opts.MinimumNoteLengthMs);

        // Validation passes
        opts.Validate();
    }

    [Theory]
    [InlineData(0.0, 0.3, 127.7)]
    [InlineData(1.0, 0.3, 127.7)]
    [InlineData(-0.1, 0.3, 127.7)]
    [InlineData(0.5, 0.0, 127.7)]
    [InlineData(0.5, 1.0, 127.7)]
    [InlineData(0.5, 0.3, 0.0)]
    [InlineData(0.5, 0.3, -10.0)]
    [InlineData(double.NaN, 0.3, 127.7)]
    [InlineData(0.5, double.PositiveInfinity, 127.7)]
    public void TranscriptionOptions_Validation_RejectsInvalidThresholds(double onset, double frame, double minLen)
    {
        var opts = new TranscriptionOptions(onset, frame, minLen);
        Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
    }

    [Fact]
    public void TranscriptionResult_SuccessContract_ExposesAllFields()
    {
        var timeline = new MusicTimeline("Piano Recital");
        timeline.AddNote(new(60, 0.0, 1.0, 80));
        timeline.AddNote(new(64, 1.0, 2.0, 85));

        var result = TranscriptionResult.Successful(
            jobId: "job_01",
            sourceAudioPath: @"C:\audio\song.wav",
            generatedMidiPath: @"C:\workspace\job_01\transcription.mid",
            timeline: timeline,
            playableNoteCount: 2,
            outOfRangeNoteCount: 0,
            minPitch: 60,
            maxPitch: 64,
            runtimeSeconds: 2.5,
            engineName: "Basic Pitch",
            engineVersion: "0.4.0"
        );

        Assert.True(result.Success);
        Assert.Equal("job_01", result.JobId);
        Assert.Equal(@"C:\audio\song.wav", result.SourceAudioPath);
        Assert.Equal(@"C:\workspace\job_01\transcription.mid", result.GeneratedMidiPath);
        Assert.NotNull(result.Timeline);
        Assert.Equal(2, result.NoteCount);
        Assert.Equal(2, result.PlayableNoteCount);
        Assert.Equal(0, result.OutOfRangeNoteCount);
        Assert.Equal(60, result.MinPitch);
        Assert.Equal(64, result.MaxPitch);
        Assert.Equal(2.5, result.RuntimeSeconds);
        Assert.Equal("Basic Pitch", result.EngineName);
        Assert.Equal("0.4.0", result.EngineVersion);
    }

    [Fact]
    public void TranscriptionResult_FailedContract_ExposesError()
    {
        var result = TranscriptionResult.Failed(
            jobId: "job_err",
            sourceAudioPath: @"C:\audio\corrupt.wav",
            errorMessage: "Inference failed",
            errorCode: "ERR_INFERENCE"
        );

        Assert.False(result.Success);
        Assert.Equal("job_err", result.JobId);
        Assert.Equal("Inference failed", result.ErrorMessage);
        Assert.Equal("ERR_INFERENCE", result.ErrorCode);
        Assert.Null(result.GeneratedMidiPath);
        Assert.Null(result.Timeline);
    }

    [Fact]
    public void TranscriptionProgress_Phases_Work()
    {
        var starting = TranscriptionProgress.Starting();
        Assert.Equal(TranscriptionPhase.WorkerStarting, starting.Phase);
        Assert.True(starting.IsIndeterminate);

        var completed = TranscriptionProgress.Completed();
        Assert.Equal(TranscriptionPhase.Completed, completed.Phase);
        Assert.False(completed.IsIndeterminate);
        Assert.Equal(1.0, completed.ProgressFraction);
    }

    [Fact]
    public void TranscriptionEngineStatus_Contracts_Work()
    {
        var avail = TranscriptionEngineStatus.Available(@"C:\python\python.exe", "3.11.2", "0.4.0");
        Assert.True(avail.IsAvailable);
        Assert.Contains("3.11.2", avail.StatusMessage);

        var unavail = TranscriptionEngineStatus.Unavailable("Python 3.11 미설치");
        Assert.False(unavail.IsAvailable);
        Assert.Contains("미설치", unavail.StatusMessage);
    }
}
