using System.Text.RegularExpressions;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RobloxPiano.Core.Music;

namespace RobloxPiano.Core.Importers;

public class MmlImporter : IMusicImporter
{
    private static readonly Regex TokenRegex = new(
        @"\s+|" +
        @"([A-Ga-g][+#-]?(?:\d+)?\.{0,5}&?)|" +
        @"([Nn]-?\d+\.{0,5}&?)|" +
        @"([Rr](?:\d+)?\.{0,5}&?)|" +
        @"([Ll]\d*\.{0,5})|" +
        @"([Oo]\d*)|" +
        @"([><])|" +
        @"([Vv]\d*)|" +
        @"([Tt]\d*)|" +
        @"(&)|" +
        @"(\S)",
        RegexOptions.Compiled);

    private static readonly Dictionary<char, int> NoteMap = new()
    {
        ['C'] = 0, ['D'] = 2, ['E'] = 4, ['F'] = 5, ['G'] = 7, ['A'] = 9, ['B'] = 11
    };

    private readonly MidiImporter _midiImporter = new();

    public IReadOnlyList<string> SupportedExtensions => new[] { ".mml", ".txt" };

    public bool CanImport(string filePathOrContent)
    {
        if (string.IsNullOrWhiteSpace(filePathOrContent))
            return false;

        var trimmed = filePathOrContent.Trim();
        if (trimmed.EndsWith(".mml", StringComparison.OrdinalIgnoreCase))
            return true;

        if (File.Exists(filePathOrContent))
        {
            try
            {
                using var reader = new StreamReader(filePathOrContent);
                char[] buffer = new char[100];
                int read = reader.Read(buffer, 0, 100);
                var prefix = new string(buffer, 0, read).Trim();
                return prefix.StartsWith("MML@", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        return trimmed.StartsWith("MML@", StringComparison.OrdinalIgnoreCase);
    }

    public MusicTimeline ImportScore(string filePathOrContent, IDictionary<string, object>? options = null)
    {
        string mmlText;
        if (File.Exists(filePathOrContent))
        {
            mmlText = File.ReadAllText(filePathOrContent);
        }
        else
        {
            mmlText = filePathOrContent;
        }

        if (options != null && options.TryGetValue("out_midi_path", out var outPathObj) && outPathObj is string outPath && !string.IsNullOrEmpty(outPath))
        {
            ConvertToMidi(mmlText, outPath);
            return _midiImporter.ImportScore(outPath);
        }

        var (mid, _) = ParseToMidi(mmlText);
        using var ms = new MemoryStream();
        mid.Write(ms);
        ms.Position = 0;
        return _midiImporter.ImportFromStream(ms, "MML Score");
    }

    public void ConvertToMidi(string mmlText, string outFilepath)
    {
        var (mid, _) = ParseToMidi(mmlText);
        if (!string.Equals(outFilepath, "NUL", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(outFilepath, "/dev/null", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(outFilepath));
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
            mid.Write(outFilepath, true);
        }
    }

    public Dictionary<string, object> ExtractMetadata(string mmlText)
    {
        var (_, stats) = ParseToMidi(mmlText);
        return stats;
    }

    private class ActiveNoteState
    {
        public int Pitch { get; set; }
        public int StartTick { get; set; }
        public int Duration { get; set; }
        public int Velocity { get; set; }

        public ActiveNoteState(int pitch, int startTick, int duration, int velocity)
        {
            Pitch = pitch;
            StartTick = startTick;
            Duration = duration;
            Velocity = velocity;
        }
    }

    public (MidiFile File, Dictionary<string, object> Metadata) ParseToMidi(string mmlText)
    {
        var cleanText = mmlText.Trim();
        if (cleanText.StartsWith("MML@", StringComparison.OrdinalIgnoreCase))
        {
            cleanText = cleanText[4..].Trim();
        }
        if (cleanText.EndsWith(";"))
        {
            cleanText = cleanText[..^1].Trim();
        }

        var tracksMml = cleanText.Split(',');
        var mid = new MidiFile
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        var globalTempoMap = new Dictionary<int, int>
        {
            [0] = 120
        };

        int totalNotes = 0;
        int minPitch = 127;
        int maxPitch = 0;

        var trackEventsList = new List<List<(int AbsTick, int Priority, MidiEvent Message)>>();

        for (int trackIdx = 0; trackIdx < tracksMml.Length; trackIdx++)
        {
            var trackStr = tracksMml[trackIdx];
            var rawEvents = new List<(int AbsTick, int Priority, MidiEvent Message)>();

            int octave = 4;
            int defaultLength = 4;
            int defaultDots = 0;
            int volume = 100;
            int currentTick = 0;

            ActiveNoteState? activeNote = null;
            bool pendingTie = false;

            int CalcDuration(int? lenNum, int dots, bool explicitDots = false)
            {
                int baseLen = (lenNum.HasValue && lenNum.Value > 0) ? lenNum.Value : defaultLength;
                int dCount = (lenNum.HasValue || explicitDots) ? dots : defaultDots;

                int baseTicks = Math.Max(1, (int)(480.0 * 4.0 / baseLen));
                int ticks = baseTicks;
                int add = baseTicks / 2;
                for (int i = 0; i < dCount; i++)
                {
                    ticks += Math.Max(1, add);
                    add /= 2;
                }
                return Math.Max(1, ticks);
            }

            void CommitActiveNote()
            {
                if (activeNote != null)
                {
                    int p = activeNote.Pitch;
                    int st = activeNote.StartTick;
                    int dur = activeNote.Duration <= 0 ? 1 : activeNote.Duration;
                    int vel = activeNote.Velocity;

                    // Priority: 2 for note_on, 1 for note_off
                    rawEvents.Add((st, 2, new NoteOnEvent((SevenBitNumber)p, (SevenBitNumber)vel)));
                    rawEvents.Add((st + dur, 1, new NoteOffEvent((SevenBitNumber)p, (SevenBitNumber)0)));

                    totalNotes++;
                    if (p < minPitch) minPitch = p;
                    if (p > maxPitch) maxPitch = p;

                    activeNote = null;
                }
            }

            int pos = 0;
            while (pos < trackStr.Length)
            {
                var match = TokenRegex.Match(trackStr, pos);
                if (!match.Success || match.Index != pos)
                {
                    throw new MmlParseException(trackIdx, pos, trackStr[pos].ToString(), "구문 오류 (Syntax error)");
                }

                var noteTok = match.Groups[1].Success ? match.Groups[1].Value : null;
                var numNoteTok = match.Groups[2].Success ? match.Groups[2].Value : null;
                var restTok = match.Groups[3].Success ? match.Groups[3].Value : null;
                var lenTok = match.Groups[4].Success ? match.Groups[4].Value : null;
                var octTok = match.Groups[5].Success ? match.Groups[5].Value : null;
                var shiftTok = match.Groups[6].Success ? match.Groups[6].Value : null;
                var volTok = match.Groups[7].Success ? match.Groups[7].Value : null;
                var tempoTok = match.Groups[8].Success ? match.Groups[8].Value : null;
                var standaloneTie = match.Groups[9].Success ? match.Groups[9].Value : null;
                var invalidTok = match.Groups[10].Success ? match.Groups[10].Value : null;

                if (invalidTok != null)
                {
                    throw new MmlParseException(trackIdx, pos, invalidTok, $"지원하지 않는 토큰 '{invalidTok}'");
                }

                if (noteTok != null)
                {
                    bool isTie = noteTok.EndsWith('&');
                    var clean = isTie ? noteTok[..^1] : noteTok;
                    char cmdChar = char.ToUpperInvariant(clean[0]);

                    int pitch = (octave + 1) * 12 + NoteMap[cmdChar];
                    int idx = 1;
                    if (clean.Length > 1 && (clean[1] == '+' || clean[1] == '#' || clean[1] == '-'))
                    {
                        if (clean[1] == '+' || clean[1] == '#') pitch += 1;
                        else if (clean[1] == '-') pitch -= 1;
                        idx = 2;
                    }

                    if (pitch < 0 || pitch > 127)
                    {
                        throw new MmlParseException(trackIdx, pos, noteTok, "MIDI pitch out of bounds (음고 범위 초과: 0~127)");
                    }

                    var rem = clean[idx..];
                    var digitsMatch = Regex.Match(rem, @"^(\d+)");
                    int? lenVal = digitsMatch.Success ? int.Parse(digitsMatch.Groups[1].Value) : null;
                    var dotsStr = digitsMatch.Success ? rem[digitsMatch.Groups[1].Length..] : rem;
                    int dotsVal = dotsStr.Count(c => c == '.');
                    bool hasExplicitDots = dotsVal > 0;

                    int dur = CalcDuration(lenVal, dotsVal, explicitDots: hasExplicitDots);

                    if (activeNote != null && pendingTie && activeNote.Pitch == pitch)
                    {
                        activeNote.Duration += dur;
                    }
                    else
                    {
                        CommitActiveNote();
                        activeNote = new ActiveNoteState(pitch, currentTick, dur, volume);
                    }

                    pendingTie = isTie;
                    currentTick += dur;
                }
                else if (numNoteTok != null)
                {
                    bool isTie = numNoteTok.EndsWith('&');
                    var clean = isTie ? numNoteTok[..^1] : numNoteTok;

                    var mN = Regex.Match(clean, @"^[Nn](-?\d+)(\.*)");
                    if (!mN.Success)
                    {
                        throw new MmlParseException(trackIdx, pos, numNoteTok, "Invalid N command format (N 명령어 형식 오류)");
                    }

                    int pitch = int.Parse(mN.Groups[1].Value);
                    if (pitch < 0 || pitch > 127)
                    {
                        throw new MmlParseException(trackIdx, pos, numNoteTok, "MIDI pitch out of bounds (음고 범위 초과: 0~127)");
                    }

                    int dotsVal = mN.Groups[2].Length;
                    bool hasExplicitDots = dotsVal > 0;

                    int dur = CalcDuration(null, dotsVal, explicitDots: hasExplicitDots);

                    if (activeNote != null && pendingTie && activeNote.Pitch == pitch)
                    {
                        activeNote.Duration += dur;
                    }
                    else
                    {
                        CommitActiveNote();
                        activeNote = new ActiveNoteState(pitch, currentTick, dur, volume);
                    }

                    pendingTie = isTie;
                    currentTick += dur;
                }
                else if (restTok != null)
                {
                    bool isTie = restTok.EndsWith('&');
                    var clean = isTie ? restTok[..^1] : restTok;
                    var rem = clean[1..];
                    var digitsMatch = Regex.Match(rem, @"^(\d+)");
                    int? lenVal = digitsMatch.Success ? int.Parse(digitsMatch.Groups[1].Value) : null;
                    var dotsStr = digitsMatch.Success ? rem[digitsMatch.Groups[1].Length..] : rem;
                    int dotsVal = dotsStr.Count(c => c == '.');
                    bool hasExplicitDots = dotsVal > 0;

                    int dur = CalcDuration(lenVal, dotsVal, explicitDots: hasExplicitDots);

                    CommitActiveNote();
                    pendingTie = false;
                    currentTick += dur;
                }
                else if (lenTok != null)
                {
                    var mL = Regex.Match(lenTok, @"^[Ll](\d+)(\.*)");
                    if (mL.Success)
                    {
                        int lVal = int.Parse(mL.Groups[1].Value);
                        if (lVal > 0)
                        {
                            defaultLength = lVal;
                            defaultDots = mL.Groups[2].Length;
                        }
                    }
                }
                else if (octTok != null)
                {
                    var valStr = octTok[1..];
                    if (!string.IsNullOrEmpty(valStr) && int.TryParse(valStr, out int val))
                    {
                        octave = Math.Clamp(val, 0, 8);
                    }
                }
                else if (shiftTok != null)
                {
                    if (shiftTok == ">")
                    {
                        octave = Math.Min(8, octave + 1);
                    }
                    else if (shiftTok == "<")
                    {
                        octave = Math.Max(0, octave - 1);
                    }
                }
                else if (volTok != null)
                {
                    var valStr = volTok[1..];
                    if (!string.IsNullOrEmpty(valStr) && int.TryParse(valStr, out int vVal))
                    {
                        if (vVal < 0 || vVal > 15)
                        {
                            throw new MmlParseException(trackIdx, pos, volTok, "Volume must be 0-15 (볼륨 범위 초과: 0~15)");
                        }
                        volume = (int)(vVal * 127.0 / 15.0);
                    }
                }
                else if (tempoTok != null)
                {
                    var valStr = tempoTok[1..];
                    if (!string.IsNullOrEmpty(valStr) && int.TryParse(valStr, out int tVal))
                    {
                        if (tVal <= 0 || tVal > 500)
                        {
                            throw new MmlParseException(trackIdx, pos, tempoTok, "Tempo must be > 0 (템포 범위 초과: 1~500)");
                        }
                        globalTempoMap[currentTick] = tVal;
                    }
                }
                else if (standaloneTie != null)
                {
                    if (activeNote != null)
                    {
                        pendingTie = true;
                    }
                }

                pos = match.Index + match.Length;
            }

            CommitActiveNote();
            trackEventsList.Add(rawEvents);
        }

        // Merge Conductor Tempo events into Track 0
        var tempoItems = globalTempoMap.OrderBy(kv => kv.Key).ToList();
        int initialBpm = tempoItems.Count > 0 ? tempoItems[0].Value : 120;

        for (int trackIdx = 0; trackIdx < trackEventsList.Count; trackIdx++)
        {
            var rawEvents = trackEventsList[trackIdx];
            var trackChunk = new TrackChunk();
            mid.Chunks.Add(trackChunk);

            if (trackIdx == 0)
            {
                foreach (var (tTick, tBpm) in tempoItems)
                {
                    long microseconds = (long)Math.Round(60000000.0 / tBpm);
                    rawEvents.Add((tTick, 0, new SetTempoEvent(microseconds)));
                }
            }

            rawEvents.Sort((a, b) =>
            {
                int cmp = a.AbsTick.CompareTo(b.AbsTick);
                if (cmp != 0) return cmp;
                return a.Priority.CompareTo(b.Priority);
            });

            int prevTick = 0;
            foreach (var (absTick, _, msg) in rawEvents)
            {
                int delta = Math.Max(0, absTick - prevTick);
                msg.DeltaTime = delta;
                trackChunk.Events.Add(msg);
                prevTick = absTick;
            }
        }

        double totalDurationSeconds = 0.0;
        try
        {
            var durationMetric = mid.GetDuration<MetricTimeSpan>();
            totalDurationSeconds = durationMetric.TotalSeconds;
        }
        catch
        {
            // fallback
            totalDurationSeconds = 0.0;
        }

        if (minPitch > maxPitch)
        {
            minPitch = 0;
            maxPitch = 0;
        }

        var metadata = new Dictionary<string, object>
        {
            ["tracks"] = tracksMml.Length,
            ["bpm"] = initialBpm,
            ["tempo"] = initialBpm,
            ["duration"] = totalDurationSeconds,
            ["notes"] = totalNotes,
            ["total_notes"] = totalNotes,
            ["min_pitch"] = minPitch,
            ["max_pitch"] = maxPitch,
            ["status"] = "VALID"
        };

        return (mid, metadata);
    }
}
