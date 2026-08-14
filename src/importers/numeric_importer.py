"""
Roblox Piano Player - Numeric Musical Notation (Jianpu) Importer
"""
import os
import re
from typing import List, Optional, Tuple, Dict
from src.importers.base import BaseMusicImporter
from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline
from src.music.hand_assignment import HandAssigner


class NumericImporter(BaseMusicImporter):
    """
    Parses Numbered Musical Notation (Jianpu / 1 2 3 4 5 6 7) into a MusicTimeline.
    Supports:
      - Numbers 1-7 (Do, Re, Mi, Fa, Sol, La, Si), 0 (Rest)
      - Accidentals: #1, b3
      - Octave shift: 1' (up), 1'' (up 2), 1, (down), 1,, (down 2)
      - Duration extension: '-' (adds 1 beat)
      - Chords: [1 3 5] or [1' 3 5,]
      - Configuration: tonic (key), base_octave, bpm, beats_per_measure
    """

    KEY_OFFSETS: Dict[str, int] = {
        "C": 0, "C#": 1, "DB": 1, "D": 2, "D#": 3, "EB": 3, "E": 4,
        "F": 5, "F#": 6, "GB": 6, "G": 7, "G#": 8, "AB": 8, "A": 9,
        "A#": 10, "BB": 10, "B": 11
    }

    # Major scale intervals from tonic: Do(0), Re(2), Mi(4), Fa(5), Sol(7), La(9), Si(11)
    SCALE_INTERVALS: Dict[int, int] = {
        1: 0, 2: 2, 3: 4, 4: 5, 5: 7, 6: 9, 7: 11
    }

    @property
    def supported_extensions(self) -> List[str]:
        return [".txt", ".num", ".jianpu"]

    def can_import(self, file_path_or_content: str) -> bool:
        if file_path_or_content.endswith((".txt", ".num", ".jianpu")):
            return True
        # Check if content has jianpu patterns
        if re.search(r'\b[1-7][\'\,]*\b', file_path_or_content):
            return True
        return False

    def parse_single_symbol(
        self,
        token: str,
        tonic: str = "C",
        base_octave: int = 4
    ) -> Optional[int]:
        """
        Parses a single jianpu note token (e.g. "#4'", "1,", "b3", "5") into a MIDI pitch.
        Returns None for rest '0'.
        """
        token = token.strip()
        if not token or token == "0":
            return None

        # Check accidental
        accidental = 0
        clean_token = token
        if clean_token.startswith("#"):
            accidental = 1
            clean_token = clean_token[1:]
        elif clean_token.startswith("b") or clean_token.startswith("B"):
            accidental = -1
            clean_token = clean_token[1:]

        # Extract digit 1-7
        m = re.match(r'([1-7])([\',]*)', clean_token)
        if not m:
            return None

        digit = int(m.group(1))
        modifiers = m.group(2)

        # Count octave modifiers: "'" = +1, "," = -1
        octave_offset = modifiers.count("'") - modifiers.count(",")

        tonic_upper = tonic.upper()
        tonic_semitone = self.KEY_OFFSETS.get(tonic_upper, 0)
        scale_semitone = self.SCALE_INTERVALS.get(digit, 0)

        # MIDI Pitch = (Base Octave + 1) * 12 + Tonic + Scale Interval + Accidental + (Octave Offset * 12)
        midi_pitch = (base_octave + 1) * 12 + tonic_semitone + scale_semitone + accidental + (octave_offset * 12)
        return midi_pitch

    def import_score(
        self,
        file_path_or_content: str,
        tonic: str = "C",
        base_octave: int = 4,
        bpm: float = 120.0,
        beat_duration: float = 0.5,
        **kwargs
    ) -> MusicTimeline:
        content = file_path_or_content
        title = "Numeric Score"
        if os.path.isfile(file_path_or_content):
            title = os.path.splitext(os.path.basename(file_path_or_content))[0]
            with open(file_path_or_content, "r", encoding="utf-8", errors="ignore") as f:
                content = f.read()

        timeline = MusicTimeline(title=title)
        timeline.initial_bpm = bpm
        sec_per_beat = 60.0 / bpm

        # Tokenize by whitespace and chords [...]
        # Matches either bracketed chords [1 3 5] or single tokens
        tokens = re.findall(r'\[[^\]]+\]|[^\s\[\]]+', content)

        current_time = 0.0
        last_notes: List[NoteEvent] = []

        for token in tokens:
            token = token.strip()
            if not token:
                continue

            if token == "-":
                # Extend previous note(s) duration
                if last_notes:
                    for ln in last_notes:
                        ln.end_time += sec_per_beat
                current_time += sec_per_beat
                continue

            if token.startswith("[") and token.endswith("]"):
                # Chord
                chord_inner = token[1:-1].strip()
                sub_tokens = chord_inner.split()
                chord_notes = []
                for st in sub_tokens:
                    p = self.parse_single_symbol(st, tonic, base_octave)
                    if p is not None:
                        n = NoteEvent(
                            pitch=p,
                            start_time=current_time,
                            end_time=current_time + sec_per_beat * 0.9,
                            velocity=64,
                            source="numeric"
                        )
                        timeline.add_note(n)
                        chord_notes.append(n)
                last_notes = chord_notes
                current_time += sec_per_beat
            else:
                p = self.parse_single_symbol(token, tonic, base_octave)
                if p is not None:
                    n = NoteEvent(
                        pitch=p,
                        start_time=current_time,
                        end_time=current_time + sec_per_beat * 0.9,
                        velocity=64,
                        source="numeric"
                    )
                    timeline.add_note(n)
                    last_notes = [n]
                else:
                    # Rest 0
                    last_notes = []
                current_time += sec_per_beat

        HandAssigner.assign_hands_to_timeline(timeline)
        timeline.sort_events()
        return timeline
