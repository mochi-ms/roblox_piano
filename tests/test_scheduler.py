"""
Unit tests for Playback Scheduler in DryRun mode
"""
import time
import pytest
from src.music.events import NoteEvent
from src.music.timeline import MusicTimeline
from src.piano.mapper import RobloxPianoMapper
from src.playback.dryrun_backend import DryRunBackend
from src.playback.key_state_manager import KeyStateManager
from src.playback.chord_engine import ChordEngine
from src.playback.scheduler import PlaybackScheduler, PlaybackState


def test_scheduler_playback_completion():
    backend = DryRunBackend()
    key_state = KeyStateManager(backend)
    mapper = RobloxPianoMapper()
    engine = ChordEngine(key_state, mapper, default_hold_duration_ms=5)
    scheduler = PlaybackScheduler(engine, key_state)
    scheduler.countdown_seconds = 0  # No countdown for quick test
    scheduler.speed = 10.0  # 10x fast speed for testing

    timeline = MusicTimeline("Test Short")
    timeline.add_note(NoteEvent(pitch=60, start_time=0.0, end_time=0.05))
    timeline.add_note(NoteEvent(pitch=64, start_time=0.05, end_time=0.10))

    scheduler.set_timeline(timeline)
    scheduler.play()

    # Wait for completion (should take < 0.1s at 10x speed)
    for _ in range(20):
        if scheduler.state == PlaybackState.COMPLETED:
            break
        time.sleep(0.02)

    assert scheduler.state == PlaybackState.COMPLETED
    assert len(backend.events) > 0
    assert len(key_state.active_keys) == 0


def test_scheduler_stop_and_reset():
    backend = DryRunBackend()
    key_state = KeyStateManager(backend)
    mapper = RobloxPianoMapper()
    engine = ChordEngine(key_state, mapper)
    scheduler = PlaybackScheduler(engine, key_state)
    scheduler.countdown_seconds = 0

    timeline = MusicTimeline("Long Song")
    timeline.add_note(NoteEvent(pitch=60, start_time=0.0, end_time=1.0))
    timeline.add_note(NoteEvent(pitch=64, start_time=5.0, end_time=6.0))

    scheduler.set_timeline(timeline)
    scheduler.play()

    time.sleep(0.05)
    scheduler.stop()

    assert scheduler.state == PlaybackState.STOPPED
    assert len(key_state.active_keys) == 0
