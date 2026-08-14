"""
Roblox Piano Player - MIDI Importer
"""
import os
import mido
from typing import List, Dict, Tuple, Optional
from src.importers.base import BaseMusicImporter
from src.music.events import NoteEvent, HandType, PedalEvent
from src.music.timeline import MusicTimeline
from src.music.hand_assignment import HandAssigner


class MidiImporter(BaseMusicImporter):
    """
    Parses Standard MIDI Files (.mid, .midi) into a normalized MusicTimeline.
    Accurately tracks tempo changes and merges all tracks into an absolute time series.
    """

    @property
    def supported_extensions(self) -> List[str]:
        return [".mid", ".midi"]

    def can_import(self, file_path_or_content: str) -> bool:
        if not os.path.isfile(file_path_or_content):
            return False
        ext = os.path.splitext(file_path_or_content)[1].lower()
        return ext in self.supported_extensions

    def import_score(self, file_path_or_content: str, **kwargs) -> MusicTimeline:
        if not os.path.isfile(file_path_or_content):
            raise FileNotFoundError(f"MIDI file not found: {file_path_or_content}")

        mid = mido.MidiFile(file_path_or_content)
        title = os.path.splitext(os.path.basename(file_path_or_content))[0]
        timeline = MusicTimeline(title=title)
        timeline.metadata["ticks_per_beat"] = mid.ticks_per_beat

        # First, build a merged tempo map across all tracks (converting ticks to seconds)
        # Format 0: single track
        # Format 1: multiple simultaneous tracks
        # Format 2: multiple independent tracks (treated as separate sequences)

        # 1. Collect all tempo changes from all tracks
        # Each tempo event: (absolute_tick, tempo_in_microseconds_per_beat)
        tempo_events: List[Tuple[int, int]] = []
        time_sig: Tuple[int, int] = (4, 4)

        for track in mid.tracks:
            abs_tick = 0
            for msg in track:
                abs_tick += msg.time
                if msg.type == 'set_tempo':
                    tempo_events.append((abs_tick, msg.tempo))
                elif msg.type == 'time_signature':
                    time_sig = (msg.numerator, msg.denominator)

        timeline.time_signature = time_sig

        # Sort tempo events by absolute tick
        tempo_events.sort(key=lambda x: x[0])

        # If no tempo event at tick 0, default to 500,000 microseconds (120 BPM)
        if not tempo_events or tempo_events[0][0] > 0:
            tempo_events.insert(0, (0, 500000))

        # Filter out redundant tempo events at same tick (keep last)
        clean_tempo_events: List[Tuple[int, int]] = []
        for tick, tempo in tempo_events:
            if clean_tempo_events and clean_tempo_events[-1][0] == tick:
                clean_tempo_events[-1] = (tick, tempo)
            else:
                clean_tempo_events.append((tick, tempo))

        initial_tempo = clean_tempo_events[0][1]
        timeline.initial_bpm = round(mido.tempo2bpm(initial_tempo), 2)

        # Precompute tick to seconds conversion table
        # Each segment: (start_tick, start_second, tempo, ticks_per_beat)
        tempo_segments: List[Tuple[int, float, int]] = []
        current_time = 0.0
        prev_tick = 0
        current_tempo = clean_tempo_events[0][1]

        for tick, tempo in clean_tempo_events:
            delta_ticks = tick - prev_tick
            current_time += mido.tick2second(delta_ticks, mid.ticks_per_beat, current_tempo)
            tempo_segments.append((tick, current_time, tempo))
            prev_tick = tick
            current_tempo = tempo

        def tick_to_seconds(target_tick: int) -> float:
            # Find the segment for target_tick
            idx = 0
            for i, (seg_tick, _, _) in enumerate(tempo_segments):
                if seg_tick <= target_tick:
                    idx = i
                else:
                    break

            seg_tick, seg_time, seg_tempo = tempo_segments[idx]
            delta_ticks = target_tick - seg_tick
            return seg_time + mido.tick2second(delta_ticks, mid.ticks_per_beat, seg_tempo)

        # 2. Parse notes from each track
        for track_idx, track in enumerate(mid.tracks):
            track_name = f"Track {track_idx + 1}"
            abs_tick = 0
            # Active notes: (channel, pitch) -> (start_tick, velocity)
            active_notes: Dict[Tuple[int, int], Tuple[int, int]] = {}

            for msg in track:
                abs_tick += msg.time

                if msg.type == 'track_name':
                    name = msg.name.strip()
                    if name:
                        track_name = name

                elif msg.type == 'note_on' and msg.velocity > 0:
                    key = (msg.channel, msg.note)
                    # If this note was already active without note_off, close it now
                    if key in active_notes:
                        start_tick, vel = active_notes.pop(key)
                        start_sec = tick_to_seconds(start_tick)
                        end_sec = tick_to_seconds(abs_tick)
                        if end_sec <= start_sec:
                            end_sec = start_sec + 0.05
                        timeline.add_note(NoteEvent(
                            pitch=msg.note,
                            start_time=start_sec,
                            end_time=end_sec,
                            velocity=vel,
                            track=track_idx,
                            channel=msg.channel,
                            source="midi"
                        ))
                    active_notes[key] = (abs_tick, msg.velocity)

                elif msg.type == 'note_off' or (msg.type == 'note_on' and msg.velocity == 0):
                    key = (msg.channel, msg.note)
                    if key in active_notes:
                        start_tick, vel = active_notes.pop(key)
                        start_sec = tick_to_seconds(start_tick)
                        end_sec = tick_to_seconds(abs_tick)
                        if end_sec <= start_sec:
                            end_sec = start_sec + 0.05
                        timeline.add_note(NoteEvent(
                            pitch=msg.note,
                            start_time=start_sec,
                            end_time=end_sec,
                            velocity=vel,
                            track=track_idx,
                            channel=msg.channel,
                            source="midi"
                        ))

                elif msg.type == 'control_change' and msg.control == 64:
                    abs_sec = tick_to_seconds(abs_tick)
                    is_down = msg.value >= 64
                    timeline.add_pedal(PedalEvent(
                        time=abs_sec,
                        down=is_down,
                        value=msg.value,
                        source="midi"
                    ))

            # Close any trailing notes left on
            for (ch, pitch), (start_tick, vel) in active_notes.items():
                start_sec = tick_to_seconds(start_tick)
                end_sec = tick_to_seconds(abs_tick)
                if end_sec <= start_sec:
                    end_sec = start_sec + 0.1
                timeline.add_note(NoteEvent(
                    pitch=pitch,
                    start_time=start_sec,
                    end_time=end_sec,
                    velocity=vel,
                    track=track_idx,
                    channel=ch,
                    source="midi"
                ))

            timeline.track_names[track_idx] = track_name

        # Assign hands and sort
        HandAssigner.assign_hands_to_timeline(timeline)
        timeline.sort_events()
        return timeline
