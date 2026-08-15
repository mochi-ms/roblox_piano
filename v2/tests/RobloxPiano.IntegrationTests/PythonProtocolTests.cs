using System.Text.Json;
using Xunit;

namespace RobloxPiano.IntegrationTests;

public class PythonProtocolTests
{
    [Fact]
    public void Protocol_TranscribeRequest_SerializesCorrectly()
    {
        var req = new
        {
            type = "transcribe",
            protocol = 1,
            request_id = "req_123",
            job_id = "job_456",
            audio_path = @"C:\audio\test.wav",
            output_dir = @"C:\workspace\job_456",
            options = new
            {
                onset_threshold = 0.5,
                frame_threshold = 0.3,
                minimum_note_length_ms = 127.7
            }
        };

        string json = JsonSerializer.Serialize(req);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("transcribe", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        Assert.Equal("req_123", root.GetProperty("request_id").GetString());
        Assert.Equal("job_456", root.GetProperty("job_id").GetString());
        Assert.Equal(0.5, root.GetProperty("options").GetProperty("onset_threshold").GetDouble());
    }

    [Fact]
    public void Protocol_ResultResponse_ParsesCorrectly()
    {
        string json = @"{
            ""type"": ""result"",
            ""protocol"": 1,
            ""request_id"": ""req_123"",
            ""job_id"": ""job_456"",
            ""midi_path"": ""C:\\workspace\\job_456\\transcription.mid"",
            ""note_count"": 42,
            ""duration_seconds"": 15.5,
            ""min_pitch"": 48,
            ""max_pitch"": 84,
            ""runtime_seconds"": 3.2,
            ""engine_name"": ""Basic Pitch"",
            ""engine_version"": ""0.4.0""
        }";

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("result", root.GetProperty("type").GetString());
        Assert.Equal(42, root.GetProperty("note_count").GetInt32());
        Assert.Equal(15.5, root.GetProperty("duration_seconds").GetDouble());
        Assert.Equal(48, root.GetProperty("min_pitch").GetInt32());
        Assert.Equal(84, root.GetProperty("max_pitch").GetInt32());
        Assert.Equal(3.2, root.GetProperty("runtime_seconds").GetDouble());
    }

    [Fact]
    public void Protocol_ErrorResponse_ParsesCorrectly()
    {
        string json = @"{
            ""type"": ""error"",
            ""protocol"": 1,
            ""request_id"": ""req_123"",
            ""job_id"": ""job_456"",
            ""error_code"": ""INFERENCE_FAILED"",
            ""error_message"": ""CUDA Out of Memory""
        }";

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("error", root.GetProperty("type").GetString());
        Assert.Equal("INFERENCE_FAILED", root.GetProperty("error_code").GetString());
        Assert.Equal("CUDA Out of Memory", root.GetProperty("error_message").GetString());
    }
}
