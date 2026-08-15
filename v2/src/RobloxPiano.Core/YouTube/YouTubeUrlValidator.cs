using System.Text.RegularExpressions;

namespace RobloxPiano.Core.YouTube;

public record YouTubeUrlValidationResult(
    bool IsValid,
    string? VideoId,
    string? OriginalUrl,
    string? CanonicalUrl,
    bool IsPlaylistOnly = false,
    string? ErrorMessage = null
)
{
    public static YouTubeUrlValidationResult Valid(string originalUrl, string videoId, string canonicalUrl) =>
        new(true, videoId, originalUrl, canonicalUrl, false, null);

    public static YouTubeUrlValidationResult PlaylistOnly(string originalUrl) =>
        new(false, null, originalUrl, null, true, YouTubeError.PlaylistUnsupported);

    public static YouTubeUrlValidationResult Invalid(string? originalUrl, string errorMessage) =>
        new(false, null, originalUrl, null, false, errorMessage);
}

public static class YouTubeUrlValidator
{
    private static readonly Regex VideoIdRegex = new(@"^[a-zA-Z0-9_-]{6,64}$", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "youtu.be",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com"
    };

    public static YouTubeUrlValidationResult Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return YouTubeUrlValidationResult.Invalid(url, YouTubeError.InvalidUrl);
        }

        string trimmed = url.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return YouTubeUrlValidationResult.Invalid(trimmed, YouTubeError.InvalidUrl);
        }

        // Only HTTP and HTTPS schemes are supported
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return YouTubeUrlValidationResult.Invalid(trimmed, YouTubeError.InvalidUrl);
        }

        // Embedded credentials (http://user:pass@...) are strictly rejected
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return YouTubeUrlValidationResult.Invalid(trimmed, YouTubeError.InvalidUrl);
        }

        // Host validation
        string host = uri.Host.ToLowerInvariant();
        if (host == "localhost" || host.StartsWith("127.") || host == "::1" || host == "[::1]")
        {
            return YouTubeUrlValidationResult.Invalid(trimmed, YouTubeError.UnsupportedHost);
        }

        if (!AllowedHosts.Contains(host))
        {
            return YouTubeUrlValidationResult.Invalid(trimmed, YouTubeError.UnsupportedHost);
        }

        string path = uri.AbsolutePath;
        string query = uri.Query;
        var queryParams = ParseQueryString(query);

        string? videoId = null;

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            // Format: https://youtu.be/<videoId>
            string potentialId = path.TrimStart('/');
            int slashIdx = potentialId.IndexOf('/');
            if (slashIdx >= 0)
            {
                potentialId = potentialId.Substring(0, slashIdx);
            }

            if (!string.IsNullOrWhiteSpace(potentialId))
            {
                videoId = potentialId;
            }
        }
        else
        {
            // Host is youtube.com or variant
            if (path.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                if (queryParams.TryGetValue("v", out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    videoId = v;
                }
                else if (queryParams.ContainsKey("list"))
                {
                    return YouTubeUrlValidationResult.PlaylistOnly(trimmed);
                }
            }
            else if (path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
            {
                string remainder = path.Substring("/shorts/".Length).TrimStart('/');
                int slashIdx = remainder.IndexOf('/');
                videoId = slashIdx >= 0 ? remainder.Substring(0, slashIdx) : remainder;
            }
            else if (path.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
            {
                string remainder = path.Substring("/embed/".Length).TrimStart('/');
                int slashIdx = remainder.IndexOf('/');
                videoId = slashIdx >= 0 ? remainder.Substring(0, slashIdx) : remainder;
            }
            else if (path.Equals("/playlist", StringComparison.OrdinalIgnoreCase) || queryParams.ContainsKey("list"))
            {
                if (queryParams.TryGetValue("v", out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    videoId = v;
                }
                else
                {
                    return YouTubeUrlValidationResult.PlaylistOnly(trimmed);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(videoId) || !VideoIdRegex.IsMatch(videoId))
        {
            return YouTubeUrlValidationResult.Invalid(trimmed, YouTubeError.InvalidUrl);
        }

        string canonicalUrl = $"https://www.youtube.com/watch?v={videoId}";
        return YouTubeUrlValidationResult.Valid(trimmed, videoId, canonicalUrl);
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return dict;

        string unescaped = query.TrimStart('?');
        var pairs = unescaped.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0]);
            string val = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (!dict.ContainsKey(key))
            {
                dict[key] = val;
            }
        }
        return dict;
    }
}
