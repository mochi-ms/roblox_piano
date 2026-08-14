"""
Unit tests for PySide6 GUI Components
"""
import os
import pytest
from PySide6.QtWidgets import QApplication
from src.app.main_window import MainWindow
from src.app.floating_overlay import FloatingOverlay
from src.app.settings_window import SettingsDialog
from src.utils.config import ConfigManager
from src.playback.scheduler import PlaybackState


@pytest.fixture(scope="session")
def qapp():
    os.environ["QT_QPA_PLATFORM"] = "offscreen"
    app = QApplication.instance()
    if app is None:
        app = QApplication([])
    return app


def test_main_window_instantiation(qapp):
    window = MainWindow()
    assert window.windowTitle() == "Roblox Auto Piano Player"
    assert window.view_stack.currentIndex() == 0  # Landing view

    # Load sample demo
    window._load_sample_score()
    assert window.view_stack.currentIndex() == 1  # Switched to player view
    assert window.timeline is not None
    assert window.timeline.total_notes > 0

    window.close()


def test_floating_overlay_modes(qapp):
    overlay = FloatingOverlay()
    overlay.set_song_title("Canon in D")
    overlay.set_playback_state(PlaybackState.PLAYING)
    overlay.set_progress(10.0, 100.0)

    # Test compact toggle
    assert not overlay._is_compact
    overlay.toggle_compact_mode()
    assert overlay._is_compact
    overlay.toggle_compact_mode()
    assert not overlay._is_compact

    overlay.close()


def test_settings_dialog_instantiation(qapp):
    cfg_mgr = ConfigManager()
    dlg = SettingsDialog(cfg_mgr)
    assert dlg.windowTitle().startswith("Settings")
    dlg.close()
