using System.Text;
using System.Text.RegularExpressions;

namespace RobloxPiano.Core.Importing;

public static class SmartMmlPreprocessor
{
    private static readonly Regex CodeBlockRegex = new(
        @"^```(?:mml)?\s*([\s\S]*?)\s*```$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex MetadataTitleRegex = new(
        @"^(?:곡명|제목|제목명|Title|Name|Song\s*Title)\s*[:=]\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SectionHeaderRegex = new(
        @"^[\s\[【(]*(?:멜로디|화음\s*\d*|코드\s*\d*|반주|베이스|보컬|Melody|Chord\s*\d*|Track\s*\d*|Bass|Vocal|Accompaniment|メロディ[ー]?|和音\s*\d*|コード\s*\d*|伴奏)[\s\]】):=-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InlineSectionHeaderRegex = new(
        @"^[\s\[【(]*(?:멜로디|화음\s*\d*|코드\s*\d*|반주|베이스|보컬|Melody|Chord\s*\d*|Track\s*\d*|Bass|Vocal|Accompaniment|メロディ[ー]?|和音\s*\d*|コード\s*\d*|伴奏)[\s\]】):=-]+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SmartMmlResult Process(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return SmartMmlResult.Failed(rawInput ?? string.Empty, "입력 텍스트가 비어 있습니다.");
        }

        var diagnostics = new List<string>();
        bool modified = false;
        string text = rawInput.Trim().Trim('\uFEFF', '\u200B');

        // 1. Strip markdown code fences if present
        var codeBlockMatch = CodeBlockRegex.Match(text);
        if (codeBlockMatch.Success)
        {
            text = codeBlockMatch.Groups[1].Value.Trim();
            modified = true;
            diagnostics.Add("마크다운 코드 블록(```)을 제거했습니다.");
        }

        // 2. Extract potential title from metadata lines
        string? extractedTitle = null;
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var contentLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            // Check metadata title
            var titleMatch = MetadataTitleRegex.Match(trimmedLine);
            if (titleMatch.Success && extractedTitle == null)
            {
                extractedTitle = titleMatch.Groups[1].Value.Trim().Trim('"', '\'');
                modified = true;
                diagnostics.Add($"메타데이터에서 곡명 '{extractedTitle}'을(를) 추출했습니다.");
                continue;
            }

            // Skip other metadata lines like Composer, Author, BPM line if not MML
            if (Regex.IsMatch(trimmedLine, @"^(?:작곡|편곡|작사|아티스트|Composer|Author|Artist|BPM)\s*[:=]", RegexOptions.IgnoreCase))
            {
                modified = true;
                continue;
            }

            contentLines.Add(trimmedLine);
        }

        if (contentLines.Count == 0)
        {
            return SmartMmlResult.Failed(rawInput, "유효한 MML 내용이 없습니다.");
        }

        // 3. Check if standard MML@...; format in single line or already comma-delimited
        string combinedContent = string.Join("\n", contentLines);
        if (combinedContent.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
        {
            var cleaned = CleanExistingMmlBlock(combinedContent, out bool blockModified, out int count);
            if (blockModified)
            {
                modified = true;
                diagnostics.Add("MML 줄바꿈 및 불필요한 공백을 정규화했습니다.");
            }
            return SmartMmlResult.Succeeded(cleaned, extractedTitle, count, modified, diagnostics);
        }

        // 4. Check for section-based multi-track formats (Mabinogi-style pasted text)
        var tracks = ParseSectionTracks(contentLines, diagnostics, ref modified);

        if (tracks.Count > 0)
        {
            var sb = new StringBuilder("MML@");
            for (int i = 0; i < tracks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(tracks[i]);
            }
            sb.Append(';');

            diagnostics.Add($"{tracks.Count}개 트랙을 통합 MML 형식으로 변환했습니다.");
            return SmartMmlResult.Succeeded(sb.ToString(), extractedTitle, tracks.Count, true, diagnostics);
        }

        // 5. Fallback: Single track or comma-separated track lines without MML@ prefix
        var simpleCleaned = CleanSimpleTrackLines(contentLines, out int simpleTrackCount, ref modified);
        return SmartMmlResult.Succeeded(simpleCleaned, extractedTitle, simpleTrackCount, modified, diagnostics);
    }

    private static string CleanExistingMmlBlock(string content, out bool modified, out int trackCount)
    {
        modified = false;
        string inner = content.Trim();
        if (inner.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
        {
            inner = inner[4..];
        }
        if (inner.EndsWith(";"))
        {
            inner = inner[..^1];
        }

        // Remove newlines and excess whitespace within tracks
        var rawTracks = inner.Split(',');
        var cleanedTracks = new List<string>();
        foreach (var t in rawTracks)
        {
            var cleaned = Regex.Replace(t, @"\s+", "");
            cleanedTracks.Add(cleaned);
        }

        trackCount = cleanedTracks.Count;
        string result = "MML@" + string.Join(",", cleanedTracks) + ";";
        if (result != content)
        {
            modified = true;
        }
        return result;
    }

    private static List<string> ParseSectionTracks(List<string> lines, List<string> diagnostics, ref bool modified)
    {
        var tracks = new List<StringBuilder>();
        StringBuilder? currentTrack = null;
        bool hasSectionHeaders = false;

        foreach (var line in lines)
        {
            // Check standalone section header
            if (SectionHeaderRegex.IsMatch(line))
            {
                hasSectionHeaders = true;
                modified = true;
                currentTrack = new StringBuilder();
                tracks.Add(currentTrack);
                continue;
            }

            // Check inline section header (e.g. "멜로디: T120L4CDEF")
            var inlineMatch = InlineSectionHeaderRegex.Match(line);
            if (inlineMatch.Success)
            {
                hasSectionHeaders = true;
                modified = true;
                currentTrack = new StringBuilder();
                tracks.Add(currentTrack);
                var mmlPart = inlineMatch.Groups[1].Value;
                AppendCleanMml(currentTrack, mmlPart);
                continue;
            }

            if (currentTrack != null)
            {
                AppendCleanMml(currentTrack, line);
            }
            else
            {
                // Line before any section header
                currentTrack = new StringBuilder();
                tracks.Add(currentTrack);
                AppendCleanMml(currentTrack, line);
            }
        }

        if (!hasSectionHeaders)
        {
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var t in tracks)
        {
            var s = t.ToString().Trim();
            if (!string.IsNullOrEmpty(s))
            {
                result.Add(s);
            }
        }
        return result;
    }

    private static void AppendCleanMml(StringBuilder sb, string rawLine)
    {
        string cleaned = rawLine.Trim();
        if (cleaned.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[4..];
        }
        if (cleaned.EndsWith(";"))
        {
            cleaned = cleaned[..^1];
        }
        cleaned = Regex.Replace(cleaned, @"\s+", "");
        sb.Append(cleaned);
    }

    private static string CleanSimpleTrackLines(List<string> lines, out int trackCount, ref bool modified)
    {
        var joined = string.Join("", lines);
        joined = Regex.Replace(joined, @"\s+", "");
        if (joined.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
        {
            joined = joined[4..];
        }
        if (joined.EndsWith(";"))
        {
            joined = joined[..^1];
        }

        var trackParts = joined.Split(',');
        trackCount = trackParts.Length;
        modified = true;
        return "MML@" + joined + ";";
    }
}
