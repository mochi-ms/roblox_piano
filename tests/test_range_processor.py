"""
Unit tests for Range Processor & Octave Fit
"""
import pytest
from src.music.events import NoteEvent
from src.music.timeline import MusicTimeline
from src.music.range_processor import RangeProcessor


def test_range_analyzer_detection():
    timeline = MusicTimeline("Range Test")
    timeline.add_note(NoteEvent(pitch=24, start_time=0.0, end_time=1.0))  # C1 (Below C2)
    timeline.add_note(NoteEvent(pitch=60, start_time=0.0, end_time=1.0))  # C4 (In range)
    timeline.add_note(NoteEvent(pitch=108, start_time=0.0, end_time=1.0)) # C8 (Above C7)

    res = RangeProcessor.analyze_range(timeline, min_pitch=36, max_pitch=96)
    assert res.total_notes == 3
    assert res.in_range_count == 1
    assert res.out_of_range_count == 2
    assert res.min_pitch == 24
    assert res.max_pitch == 108


def test_octave_fit_transformation():
    timeline = MusicTimeline("Fit Test")
    n1 = NoteEvent(pitch=24, start_time=0.0, end_time=1.0)  # C1 -> C2 (36) or higher
    n2 = NoteEvent(pitch=108, start_time=0.0, end_time=1.0) # C8 -> C7 (96) or lower

    timeline.add_note(n1)
    timeline.add_note(n2)

    modified_count = RangeProcessor.apply_octave_fit(timeline, min_pitch=36, max_pitch=96)
    assert modified_count == 2

    # Verify both notes are now within [36, 96]
    assert 36 <= n1.pitch <= 96
    assert 36 <= n2.pitch <= 96
    assert n1.pitch % 12 == 0  # Still a C note
    assert n2.pitch % 12 == 0  # Still a C note
