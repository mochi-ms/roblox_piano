"""
Roblox Piano Player - MusicXML Importer
"""
import os
from typing import List, Optional
from music21 import converter, note, chord, tempo, meter
from src.importers.base import BaseMusicImporter
from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline
from src.music.hand_assignment import HandAssigner


class MusicXmlImporter(BaseMusicImporter):
    """
    Parses MusicXML files (.musicxml, .xml, .mxl) using music21 into a normalized MusicTimeline.
    """

    @property
    def supported_extensions(self) -> List[str]:
        return [".musicxml", ".xml", ".mxl"]

    def can_import(self, file_path_or_content: str) -> bool:
        if not os.path.isfile(file_path_or_content):
            return False
        ext = os.path.splitext(file_path_or_content)[1].lower()
        return ext in self.supported_extensions

    def import_score(self, file_path_or_content: str, **kwargs) -> MusicTimeline:
        if not os.path.isfile(file_path_or_content):
            raise FileNotFoundError(f"MusicXML file not found: {file_path_or_content}")

        title = os.path.splitext(os.path.basename(file_path_or_content))[0]
        timeline = MusicTimeline(title=title)

        score = converter.parse(file_path_or_content)

        # Extract initial BPM
        initial_bpm = 120.0
        metronome_marks = score.flatten().getElementsByClass(tempo.MetronomeMark)
        if metronome_marks:
            initial_bpm = float(metronome_marks[0].getQuarterBPM())
        timeline.initial_bpm = initial_bpm

        # Extract time signature
        ts_list = score.flatten().getElementsByClass(meter.TimeSignature)
        if ts_list:
            timeline.time_signature = (ts_list[0].numerator, ts_list[0].denominator)

        # Seconds per quarter note at initial BPM
        sec_per_quarter = 60.0 / initial_bpm

        # Iterate over parts
        for part_idx, part in enumerate(score.parts):
            part_name = part.partName or f"Part {part_idx + 1}"
            timeline.track_names[part_idx] = part_name

            # Process flat notes and chords in this part
            # Use offsetInHierarchy to get absolute quarter note position
            flat_elements = part.flatten()

            for elem in flat_elements:
                offset_quarters = float(elem.offset)
                start_sec = offset_quarters * sec_per_quarter
                dur_quarters = float(elem.quarterLength)
                end_sec = start_sec + (dur_quarters * sec_per_quarter)

                staff_idx = None
                # Check for staff attribute in MusicXML elements
                if hasattr(elem, 'staff') and elem.staff is not None:
                    staff_idx = elem.staff

                if isinstance(elem, note.Note):
                    # Single note
                    pitch_val = elem.pitch.midi
                    vel = elem.volume.velocity if elem.volume and elem.volume.velocity is not None else 64
                    note_ev = NoteEvent(
                        pitch=pitch_val,
                        start_time=start_sec,
                        end_time=end_sec,
                        velocity=vel,
                        staff=staff_idx,
                        track=part_idx,
                        source="musicxml"
                    )
                    timeline.add_note(note_ev)

                elif isinstance(elem, chord.Chord):
                    # Multi-note chord
                    for ch_note in elem.notes:
                        pitch_val = ch_note.pitch.midi
                        vel = ch_note.volume.velocity if ch_note.volume and ch_note.volume.velocity is not None else 64
                        note_ev = NoteEvent(
                            pitch=pitch_val,
                            start_time=start_sec,
                            end_time=end_sec,
                            velocity=vel,
                            staff=staff_idx,
                            track=part_idx,
                            source="musicxml"
                        )
                        timeline.add_note(note_ev)

        # Assign hands and sort notes
        HandAssigner.assign_hands_to_timeline(timeline)
        timeline.sort_events()
        return timeline
