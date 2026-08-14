"""
Unit tests for Mixed Chords and Physical Key Conflict resolution
"""
import pytest
from src.music.events import NoteEvent
from src.piano.mapper import RobloxPianoMapper
from src.playback.dryrun_backend import DryRunBackend
from src.playback.key_state_manager import KeyStateManager
from src.playback.chord_engine import ChordEngine, ConflictPolicy


def test_mixed_chord_shift_separation():
    backend = DryRunBackend()
    key_state = KeyStateManager(backend)
    mapper = RobloxPianoMapper()
    engine = ChordEngine(key_state=key_state, mapper=mapper, default_hold_duration_ms=10)

    # Note 1: F3 (53) -> 'q' (unshifted)
    # Note 2: G#3 (56) -> 'W' (Shift + w)
    n1 = NoteEvent(pitch=53, start_time=0.0, end_time=0.1)
    n2 = NoteEvent(pitch=56, start_time=0.0, end_time=0.1)

    engine.play_chord_notes([n1, n2])

    events = backend.events
    # Check sequence
    # 1. 'q' down
    # 2. 'shift' down
    # 3. 'w' down
    # 4. 'w' up
    # 5. 'shift' up
    # 6. 'q' up
    action_sequence = [(act, key) for _, act, key in events]

    assert ("down", "q") in action_sequence
    assert ("down", "shift") in action_sequence
    assert ("down", "w") in action_sequence

    # Verify 'q' down happens before 'shift' down
    q_down_idx = action_sequence.index(("down", "q"))
    shift_down_idx = action_sequence.index(("down", "shift"))
    assert q_down_idx < shift_down_idx

    # Verify 'shift' up happens before 'q' up
    shift_up_idx = action_sequence.index(("up", "shift"))
    q_up_idx = action_sequence.index(("up", "q"))
    assert shift_up_idx < q_up_idx

    # Finally verify all keys are released
    assert len(key_state.active_keys) == 0
    assert not key_state.shift_active


def test_same_physical_key_conflict_micro_arpeggio():
    backend = DryRunBackend()
    key_state = KeyStateManager(backend)
    mapper = RobloxPianoMapper()
    engine = ChordEngine(
        key_state=key_state,
        mapper=mapper,
        conflict_policy=ConflictPolicy.MICRO_ARPEGGIO,
        conflict_delay_ms=5,
        default_hold_duration_ms=10
    )

    # Note 1: F3 (53) -> 'q' (unshifted)
    # Note 2: F#3 (54) -> 'Q' (Shift + q) - SAME PHYSICAL KEY 'q'!
    n1 = NoteEvent(pitch=53, start_time=0.0, end_time=0.1)
    n2 = NoteEvent(pitch=54, start_time=0.0, end_time=0.1)

    engine.play_chord_notes([n1, n2])

    events = backend.events
    action_sequence = [(act, key) for _, act, key in events]

    # Verify 'q' is pressed, then released BEFORE 'shift' is pressed for 'Q'
    assert ("down", "q") in action_sequence
    first_q_down = action_sequence.index(("down", "q"))
    first_q_up = action_sequence.index(("up", "q"))
    shift_down = action_sequence.index(("down", "shift"))

    assert first_q_down < first_q_up < shift_down
    assert len(key_state.active_keys) == 0
    assert not key_state.shift_active
