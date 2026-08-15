using System.Globalization;

namespace RobloxPiano.Infrastructure.Audio;

public class FfmpegProgressParser
{
    private readonly double _totalDurationSeconds;

    public FfmpegProgressParser(double totalDurationSeconds)
    {
        _totalDurationSeconds = totalDurationSeconds > 0 ? totalDurationSeconds : 1.0;
    }

    public double? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        line = line.Trim();

        // Check progress=end
        if (string.Equals(line, "progress=end", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        int eqIdx = line.IndexOf('=');
        if (eqIdx <= 0 || eqIdx >= line.Length - 1) return null;

        string key = line[..eqIdx].Trim();
        string val = line[(eqIdx + 1)..].Trim();

        if (string.Equals(key, "out_time_us", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long outUs))
            {
                double currentSec = outUs / 1_000_000.0;
                return Math.Clamp(currentSec / _totalDurationSeconds, 0.0, 1.0);
            }
        }
        else if (string.Equals(key, "out_time_ms", StringComparison.OrdinalIgnoreCase))
        {
            // Note: in many ffmpeg builds, out_time_ms is actually in microseconds despite the key name
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long outMs))
            {
                double currentSec = outMs > 1_000_000 ? outMs / 1_000_000.0 : outMs / 1_000.0;
                return Math.Clamp(currentSec / _totalDurationSeconds, 0.0, 1.0);
            }
        }
        else if (string.Equals(key, "out_time", StringComparison.OrdinalIgnoreCase))
        {
            if (TimeSpan.TryParse(val, CultureInfo.InvariantCulture, out var ts))
            {
                return Math.Clamp(ts.TotalSeconds / _totalDurationSeconds, 0.0, 1.0);
            }
        }

        return null;
    }
}
