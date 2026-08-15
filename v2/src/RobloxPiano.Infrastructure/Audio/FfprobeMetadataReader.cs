using System.Globalization;
using System.IO;
using System.Text.Json;
using RobloxPiano.Core.Audio;

namespace RobloxPiano.Infrastructure.Audio;

public class FfprobeMetadataReader
{
    private readonly IFfmpegProcessRunner _runner;

    public const double MaxSupportedDurationSeconds = 1800.0; // 30 minutes limit

    public FfprobeMetadataReader(IFfmpegProcessRunner? runner = null)
    {
        _runner = runner ?? new FfmpegProcessRunner();
    }

    public async Task<AudioValidationResult> ProbeFileAsync(
        string ffprobePath,
        string filePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return AudioValidationResult.Invalid(AudioError.FileNotFound);
        }

        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            return AudioValidationResult.Invalid(AudioError.FfprobeNotFound);
        }

        var arguments = new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            filePath
        };

        var result = await _runner.RunProcessAsync(
            ffprobePath,
            arguments,
            timeout: TimeSpan.FromSeconds(15),
            ct: ct);

        if (result.IsCancelled || ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            string err = !string.IsNullOrWhiteSpace(result.StandardError)
                ? $"{AudioError.InvalidMedia}: {result.StandardError.Trim()}"
                : AudioError.InvalidMedia;
            return AudioValidationResult.Invalid(err);
        }

        return ParseFfprobeJson(result.StandardOutput, filePath);
    }

    public static AudioValidationResult ParseFfprobeJson(string jsonContent, string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // 1. Streams
            if (!root.TryGetProperty("streams", out var streamsElement) || streamsElement.ValueKind != JsonValueKind.Array)
            {
                return AudioValidationResult.Invalid(AudioError.NoAudioStream);
            }

            JsonElement? firstAudioStream = null;
            int audioStreamCount = 0;

            foreach (var stream in streamsElement.EnumerateArray())
            {
                if (stream.TryGetProperty("codec_type", out var codecType) &&
                    string.Equals(codecType.GetString(), "audio", StringComparison.OrdinalIgnoreCase))
                {
                    audioStreamCount++;
                    firstAudioStream ??= stream;
                }
            }

            if (firstAudioStream == null || audioStreamCount == 0)
            {
                return AudioValidationResult.Invalid(AudioError.NoAudioStream);
            }

            var audio = firstAudioStream.Value;

            string codecName = audio.TryGetProperty("codec_name", out var codecProp)
                ? codecProp.GetString() ?? "unknown"
                : "unknown";

            int channels = audio.TryGetProperty("channels", out var channelsProp) && channelsProp.TryGetInt32(out int ch)
                ? ch
                : 1;

            int sampleRate = 0;
            if (audio.TryGetProperty("sample_rate", out var srProp))
            {
                if (srProp.ValueKind == JsonValueKind.Number && srProp.TryGetInt32(out int srNum))
                {
                    sampleRate = srNum;
                }
                else if (srProp.ValueKind == JsonValueKind.String && int.TryParse(srProp.GetString(), out int srStr))
                {
                    sampleRate = srStr;
                }
            }

            long? streamBitRate = null;
            if (audio.TryGetProperty("bit_rate", out var brProp))
            {
                if (brProp.ValueKind == JsonValueKind.Number && brProp.TryGetInt64(out long brNum))
                {
                    streamBitRate = brNum;
                }
                else if (brProp.ValueKind == JsonValueKind.String && long.TryParse(brProp.GetString(), out long brStr))
                {
                    streamBitRate = brStr;
                }
            }

            // 2. Format
            if (!root.TryGetProperty("format", out var formatElement) || formatElement.ValueKind != JsonValueKind.Object)
            {
                return AudioValidationResult.Invalid(AudioError.InvalidMedia);
            }

            string containerFormat = formatElement.TryGetProperty("format_name", out var fnProp)
                ? fnProp.GetString() ?? "unknown"
                : "unknown";

            double duration = 0.0;
            if (formatElement.TryGetProperty("duration", out var durProp))
            {
                if (durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out double durNum))
                {
                    duration = durNum;
                }
                else if (durProp.ValueKind == JsonValueKind.String && double.TryParse(durProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double durStr))
                {
                    duration = durStr;
                }
            }
            else if (audio.TryGetProperty("duration", out var audioDurProp))
            {
                if (audioDurProp.ValueKind == JsonValueKind.Number && audioDurProp.TryGetDouble(out double aDurNum))
                {
                    duration = aDurNum;
                }
                else if (audioDurProp.ValueKind == JsonValueKind.String && double.TryParse(audioDurProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double aDurStr))
                {
                    duration = aDurStr;
                }
            }

            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
            {
                return AudioValidationResult.Invalid(AudioError.InvalidMedia);
            }

            if (duration > MaxSupportedDurationSeconds)
            {
                return AudioValidationResult.Invalid(AudioError.TooLong);
            }

            long fileSizeBytes = 0;
            if (formatElement.TryGetProperty("size", out var sizeProp))
            {
                if (sizeProp.ValueKind == JsonValueKind.Number && sizeProp.TryGetInt64(out long sizeNum))
                {
                    fileSizeBytes = sizeNum;
                }
                else if (sizeProp.ValueKind == JsonValueKind.String && long.TryParse(sizeProp.GetString(), out long sizeStr))
                {
                    fileSizeBytes = sizeStr;
                }
            }

            if (fileSizeBytes <= 0 && File.Exists(filePath))
            {
                fileSizeBytes = new FileInfo(filePath).Length;
            }

            long? formatBitRate = null;
            if (formatElement.TryGetProperty("bit_rate", out var fbrProp))
            {
                if (fbrProp.ValueKind == JsonValueKind.Number && fbrProp.TryGetInt64(out long fbrNum))
                {
                    formatBitRate = fbrNum;
                }
                else if (fbrProp.ValueKind == JsonValueKind.String && long.TryParse(fbrProp.GetString(), out long fbrStr))
                {
                    formatBitRate = fbrStr;
                }
            }

            string? title = null;
            string? artist = null;
            if (formatElement.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var tag in tagsProp.EnumerateObject())
                {
                    if (string.Equals(tag.Name, "title", StringComparison.OrdinalIgnoreCase))
                    {
                        title = tag.Value.GetString();
                    }
                    else if (string.Equals(tag.Name, "artist", StringComparison.OrdinalIgnoreCase))
                    {
                        artist = tag.Value.GetString();
                    }
                }
            }

            var metadata = new AudioMetadata(
                SourcePath: filePath,
                ContainerFormat: containerFormat,
                CodecName: codecName,
                DurationSeconds: duration,
                SampleRate: sampleRate,
                Channels: channels,
                BitRate: streamBitRate ?? formatBitRate,
                FileSizeBytes: fileSizeBytes,
                AudioStreamCount: audioStreamCount,
                Title: title,
                Artist: artist
            );

            return AudioValidationResult.Valid(metadata);
        }
        catch (Exception ex)
        {
            return AudioValidationResult.Invalid($"{AudioError.InvalidMedia}: {ex.Message}");
        }
    }
}
