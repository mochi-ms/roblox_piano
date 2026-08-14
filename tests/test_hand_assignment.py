"""
Unit tests for Hand Assignment logic
"""
import pytest
from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline
from src.music.hand_assignment import HandAssigner


def test_hand_assignment_pitch_split():
    timeline = MusicTimeline()
    n_bass = NoteEvent(pitch=48, start_time=0.0, end_time=1.0)  # C3
    n_treble = NoteEvent(pitch=72, start_time=0.0, end_time=1.0) # C5

    timeline.add_note(n_bass)
    timeline.add_note(n_treble)

    HandAssigner.assign_hands_to_timeline(timeline, split_point=60)

    assert n_bass.hand == HandType.LEFT
    assert n_treble.hand == HandType.RIGHT


def test_hand_assignment_staff_override():
    timeline = MusicTimeline()
    n1 = NoteEvent(pitch=72, start_time=0.0, end_time=1.0, staff=2)  # Staff 2 -> LH even if high pitch
    n2 = NoteEvent(pitch=48, start_time=0.0, end_time=1.0, staff=1)  # Staff 1 -> RH even if low pitch

    timeline.add_note(n1)
    timeline.add_note(n2)

    HandAssigner.assign_hands_to_timeline(timeline)

    assert n1.hand == HandType.LEFT
    assert n2.hand == HandType.RIGHT
