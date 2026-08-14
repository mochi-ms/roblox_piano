"""
Unit tests for Key State Safety & Emergency Stop
"""
import pytest
from src.playback.dryrun_backend import DryRunBackend
from src.playback.key_state_manager import KeyStateManager


def test_emergency_release_all():
    backend = DryRunBackend()
    mgr = KeyStateManager(backend)

    # Press multiple keys & Shift
    mgr.press_physical_key("q")
    mgr.press_physical_key("w")
    mgr.set_modifier("SHIFT", True)

    assert "q" in mgr.active_keys
    assert "w" in mgr.active_keys
    assert "SHIFT" in mgr.active_modifiers

    # Call emergency release
    mgr.release_all()

    assert len(mgr.active_keys) == 0
    assert "SHIFT" not in mgr.active_modifiers
    assert len(backend.pressed_keys) == 0
