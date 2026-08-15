using RobloxPiano.Core.Audio;
using RobloxPiano.Infrastructure.Audio;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class AudioIngestValidationTests : IDisposable
{
    private readonly string _tempDir;

    public AudioIngestValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rp_audio_val_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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

    [Fact]
    public async Task AudioIngest_MissingFile_Fails()
    {
        var service = new AudioIngestionService();
        var req = new AudioIngestRequest(Path.Combine(_tempDir, "non_existent.mp3"));

        var result = await service.IngestAudioAsync(req);

        Assert.False(result.Success);
        Assert.Equal(AudioError.FileNotFound, result.ErrorMessage);
    }

    [Fact]
    public async Task AudioIngest_UnsupportedExtension_Fails()
    {
        string textFile = Path.Combine(_tempDir, "document.pdf");
        await File.WriteAllTextAsync(textFile, "Not audio");

        var service = new AudioIngestionService();
        var req = new AudioIngestRequest(textFile);

        var result = await service.IngestAudioAsync(req);

        Assert.False(result.Success);
        Assert.Equal(AudioError.UnsupportedExtension, result.ErrorMessage);
    }

    [Fact]
    public void FfprobeParser_TooLong_Rejects()
    {
        string json = @"
{
    ""streams"": [
        {
            ""codec_type"": ""audio"",
            ""codec_name"": ""mp3"",
            ""channels"": 2,
            ""sample_rate"": ""44100""
        }
    ],
    ""format"": {
        ""format_name"": ""mp3"",
        ""duration"": ""1801.0"",
        ""size"": ""50000000""
    }
}";

        var result = FfprobeMetadataReader.ParseFfprobeJson(json, @"C:\Music\marathon.mp3");

        Assert.False(result.IsValid);
        Assert.Equal(AudioError.TooLong, result.ErrorMessage);
    }
}
