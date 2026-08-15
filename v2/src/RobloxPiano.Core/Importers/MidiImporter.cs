using Melanchall.DryWetMidi.Core;
using RobloxPiano.Core.Music;
using MusicNoteEvent = RobloxPiano.Core.Music.NoteEvent;

namespace RobloxPiano.Core.Importers;

public class MidiImporter : IMusicImporter
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".mid", ".midi" };

    public bool CanImport(string filePathOrContent)
    {
        if (string.IsNullOrWhiteSpace(filePathOrContent))
            return false;

        if (!File.Exists(filePathOrContent))
            return false;

        var ext = Path.GetExtension(filePathOrContent);
        return SupportedExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    public MusicTimeline ImportScore(string filePathOrContent, IDictionary<string, object>? options = null)
    {
        if (!File.Exists(filePathOrContent))
            throw new FileNotFoundException($"MIDI file not found: {filePathOrContent}", filePathOrContent);

        using var stream = File.OpenRead(filePathOrContent);
        return ImportFromStream(stream, Path.GetFileNameWithoutExtension(filePathOrContent));
    }

    public MusicTimeline ImportFromStream(Stream stream, string title = "Untitled")
    {
        var midiFile = MidiFile.Read(stream);
        var timeline = new MusicTimeline(title);

        short ticksPerBeat = 480;
        if (midiFile.TimeDivision is TicksPerQuarterNoteTimeDivision tpq)
        {
            ticksPerBeat = tpq.TicksPerQuarterNote;
        }
        timeline.Metadata["ticks_per_beat"] = (int)ticksPerBeat;

        // 1. Collect all tempo changes from all tracks
        var tempoEvents = new List<(long Tick, long TempoMicroseconds)>();
        (int Numerator, int Denominator) timeSig = (4, 4);

        foreach (var trackChunk in midiFile.GetTrackChunks())
        {
            long absTick = 0;
            foreach (var midiEvent in trackChunk.Events)
            {
                absTick += midiEvent.DeltaTime;
                if (midiEvent is SetTempoEvent setTempo)
                {
                    tempoEvents.Add((absTick, setTempo.MicrosecondsPerQuarterNote));
                }
                else if (midiEvent is TimeSignatureEvent ts)
                {
                    timeSig = (ts.Numerator, ts.Denominator);
                }
            }
        }

        timeline.TimeSignature = timeSig;

        // Sort by absolute tick
        tempoEvents.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        // Default to 120 BPM (500,000 microseconds) at tick 0 if missing
        if (tempoEvents.Count == 0 || tempoEvents[0].Tick > 0)
        {
            tempoEvents.Insert(0, (0, 500000));
        }

        // Deduplicate at same tick (keep last)
        var cleanTempoEvents = new List<(long Tick, long TempoMicroseconds)>();
        foreach (var (tick, tempo) in tempoEvents)
        {
            if (cleanTempoEvents.Count > 0 && cleanTempoEvents[^1].Tick == tick)
            {
                cleanTempoEvents[^1] = (tick, tempo);
            }
            else
            {
                cleanTempoEvents.Add((tick, tempo));
            }
        }

        long initialTempo = cleanTempoEvents[0].TempoMicroseconds;
        timeline.InitialBpm = Math.Round(60000000.0 / initialTempo, 2);

        // Precompute tempo segments
        var tempoSegments = new List<(long StartTick, double StartSecond, long TempoMicroseconds)>();
        double currentSecond = 0.0;
        long prevTick = 0;
        long currentTempo = cleanTempoEvents[0].TempoMicroseconds;

        foreach (var (tick, tempo) in cleanTempoEvents)
        {
            long deltaTicks = tick - prevTick;
            currentSecond += (double)deltaTicks * currentTempo / (ticksPerBeat * 1000000.0);
            tempoSegments.Add((tick, currentSecond, tempo));
            prevTick = tick;
            currentTempo = tempo;
        }

        double TickToSeconds(long targetTick)
        {
            int idx = 0;
            for (int i = 0; i < tempoSegments.Count; i++)
            {
                if (tempoSegments[i].StartTick <= targetTick)
                {
                    idx = i;
                }
                else
                {
                    break;
                }
            }

            var seg = tempoSegments[idx];
            long deltaTicks = targetTick - seg.StartTick;
            return seg.StartSecond + (double)deltaTicks * seg.TempoMicroseconds / (ticksPerBeat * 1000000.0);
        }

        // 2. Parse notes from each track
        var trackChunks = midiFile.GetTrackChunks().ToList();
        for (int trackIdx = 0; trackIdx < trackChunks.Count; trackIdx++)
        {
            var trackChunk = trackChunks[trackIdx];
            string trackName = $"Track {trackIdx + 1}";
            long absTick = 0;

            // Active notes: (Channel, NoteNumber) -> (StartTick, Velocity)
            var activeNotes = new Dictionary<(int Channel, int NoteNumber), (long StartTick, int Velocity)>();

            foreach (var midiEvent in trackChunk.Events)
            {
                absTick += midiEvent.DeltaTime;

                if (midiEvent is SequenceTrackNameEvent trackNameEvent)
                {
                    var text = trackNameEvent.Text?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        trackName = text;
                    }
                }
                else if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
                {
                    var key = (noteOn.Channel, (int)noteOn.NoteNumber);
                    if (activeNotes.TryGetValue(key, out var active))
                    {
                        activeNotes.Remove(key);
                        double startSec = TickToSeconds(active.StartTick);
                        double endSec = TickToSeconds(absTick);
                        if (endSec <= startSec)
                        {
                            endSec = startSec + 0.05;
                        }

                        timeline.AddNote(new MusicNoteEvent(
                            pitch: key.Item2,
                            startTime: startSec,
                            endTime: endSec,
                            velocity: active.Velocity,
                            track: trackIdx,
                            channel: noteOn.Channel,
                            source: "midi"
                        ));
                    }

                    activeNotes[key] = (absTick, noteOn.Velocity);
                }
                else if (midiEvent is NoteOffEvent noteOff || (midiEvent is NoteOnEvent noteOnZero && noteOnZero.Velocity == 0))
                {
                    int channel = midiEvent is NoteOffEvent off ? off.Channel : ((NoteOnEvent)midiEvent).Channel;
                    int noteNumber = midiEvent is NoteOffEvent offN ? offN.NoteNumber : ((NoteOnEvent)midiEvent).NoteNumber;

                    var key = (channel, noteNumber);
                    if (activeNotes.TryGetValue(key, out var active))
                    {
                        activeNotes.Remove(key);
                        double startSec = TickToSeconds(active.StartTick);
                        double endSec = TickToSeconds(absTick);
                        if (endSec <= startSec)
                        {
                            endSec = startSec + 0.05;
                        }

                        timeline.AddNote(new MusicNoteEvent(
                            pitch: noteNumber,
                            startTime: startSec,
                            endTime: endSec,
                            velocity: active.Velocity,
                            track: trackIdx,
                            channel: channel,
                            source: "midi"
                        ));
                    }
                }
                else if (midiEvent is ControlChangeEvent cc && cc.ControlNumber == 64)
                {
                    double absSec = TickToSeconds(absTick);
                    bool isDown = cc.ControlValue >= 64;
                    timeline.AddPedal(new PedalEvent(
                        time: absSec,
                        down: isDown,
                        value: cc.ControlValue,
                        source: "midi"
                    ));
                }
            }

            // Close trailing active notes
            foreach (var ((ch, pitch), (startTick, vel)) in activeNotes)
            {
                double startSec = TickToSeconds(startTick);
                double endSec = TickToSeconds(absTick);
                if (endSec <= startSec)
                {
                    endSec = startSec + 0.1;
                }

                timeline.AddNote(new MusicNoteEvent(
                    pitch: pitch,
                    startTime: startSec,
                    endTime: endSec,
                    velocity: vel,
                    track: trackIdx,
                    channel: ch,
                    source: "midi"
                ));
            }

            timeline.TrackNames[trackIdx] = trackName;
        }

        HandAssignmentService.AssignHandsToTimeline(timeline);
        timeline.SortEvents();
        return timeline;
    }
}
