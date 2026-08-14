"""
Roblox Piano Player - Range Analyzer & Octave Fit Engine
"""
from dataclasses import dataclass
from typing import List, Tuple
from src.music.events import NoteEvent
from src.music.timeline import MusicTimeline


@dataclass
class RangeAnalysisResult:
    total_notes: int
    in_range_count: int
    out_of_range_count: int
    min_pitch: int
    max_pitch: int
    out_of_range_notes: List[NoteEvent]
    suggested_transpose: int = 0


class RangeProcessor:
    """
    Analyzes notes against the Roblox 61-key piano range (C2=36 to C7=96).
    Provides non-destructive octave fitting to pull out-of-range notes into playable range.
    """

    DEFAULT_MIN_PITCH = 36  # C2
    DEFAULT_MAX_PITCH = 96  # C7

    @classmethod
    def analyze_range(
        cls,
        timeline: MusicTimeline,
        min_pitch: int = DEFAULT_MIN_PITCH,
        max_pitch: int = DEFAULT_MAX_PITCH
    ) -> RangeAnalysisResult:
        if not timeline.notes:
            return RangeAnalysisResult(0, 0, 0, 60, 60, [])

        out_notes = []
        pitches = []
        for n in timeline.notes:
            pitches.append(n.pitch)
            if not (min_pitch <= n.pitch <= max_pitch):
                out_notes.append(n)

        min_p = min(pitches)
        max_p = max(pitches)

        # Suggest optimal transpose if all notes can fit within span of 60 semitones
        span = max_p - min_p
        suggested = 0
        if span <= (max_pitch - min_pitch):
            if min_p < min_pitch:
                suggested = min_pitch - min_p
            elif max_p > max_pitch:
                suggested = max_pitch - max_p

        return RangeAnalysisResult(
            total_notes=len(timeline.notes),
            in_range_count=len(timeline.notes) - len(out_notes),
            out_of_range_count=len(out_notes),
            min_pitch=min_p,
            max_pitch=max_p,
            out_of_range_notes=out_notes,
            suggested_transpose=suggested
        )

    @classmethod
    def apply_octave_fit(
        cls,
        timeline: MusicTimeline,
        min_pitch: int = DEFAULT_MIN_PITCH,
        max_pitch: int = DEFAULT_MAX_PITCH
    ) -> int:
        """
        Adjusts each out-of-range note by +-12 (octaves) until it fits in [min_pitch, max_pitch].
        Returns the number of modified notes.
        """
        modified_count = 0
        for n in timeline.notes:
            if n.original_pitch is None:
                n.original_pitch = n.pitch

            curr_pitch = n.pitch
            adjusted = False

            while curr_pitch < min_pitch:
                curr_pitch += 12
                adjusted = True

            while curr_pitch > max_pitch:
                curr_pitch -= 12
                adjusted = True

            # If still somehow outside (e.g. edge cases), clamp to boundaries
            if curr_pitch < min_pitch:
                curr_pitch = min_pitch
                adjusted = True
            elif curr_pitch > max_pitch:
                curr_pitch = max_pitch
                adjusted = True

            if adjusted:
                n.pitch = curr_pitch
                modified_count += 1

        return modified_count
