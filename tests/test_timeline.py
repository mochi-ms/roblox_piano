"""
Unit tests for MusicTimeline and Event models
"""
import pytest
from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline


def test_timeline_duration_and_sort():
    timeline = MusicTimeline(title="Test Song")
    n1 = NoteEvent(pitch=60, start_time=1.0, end_time=2.0)
    n2 = NoteEvent(pitch=64, start_time=0.0, end_time=0.5)
    n3 = NoteEvent(pitch=67, start_time=0.0, end_time=1.0)

    timeline.add_note(n1)
    timeline.add_note(n2)
    timeline.add_note(n3)

    timeline.sort_events()

    assert timeline.total_notes == 3
    assert timeline.duration == 2.0
    assert timeline.pitch_range == (60, 67)
    assert timeline.notes[0].pitch == 64
    assert timeline.notes[1].pitch == 67
    assert timeline.notes[2].pitch == 60


def test_chord_grouping_tolerance():
    timeline = MusicTimeline()
    # 3 notes starting almost simultaneously (within 10ms)
    n1 = NoteEvent(pitch=48, start_time=0.000, end_time=1.0)
    n2 = NoteEvent(pitch=60, start_time=0.005, end_time=1.0)
    n3 = NoteEvent(pitch=64, start_time=0.010, end_time=1.0)
    # 1 note starting at 0.5s
    n4 = NoteEvent(pitch=67, start_time=0.500, end_time=1.0)

    timeline.add_note(n1)
    timeline.add_note(n2)
    timeline.add_note(n3)
    timeline.add_note(n4)

    chords = timeline.build_chord_groups(tolerance=0.015)
    assert len(chords) == 2
    assert len(chords[0].notes) == 3
    assert len(chords[1].notes) == 1
    assert chords[0].start_time == 0.000
    assert chords[1].start_time == 0.500
