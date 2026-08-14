import pytest
from PySide6.QtWidgets import QApplication
from src.app.main_window import MainWindow
import sys

@pytest.fixture(scope="session")
def qapp():
    app = QApplication.instance()
    if app is None:
        app = QApplication(sys.argv)
    return app

def test_overlay_hidden_on_startup(qapp):
    win = MainWindow()
    # 1. Overlay MUST be hidden on startup
    assert not win.overlay.isVisible()
    # 2. Toggle button unchecked
    assert not win.btn_overlay_toggle.isChecked()
    win.close()

def test_overlay_f4_toggle(qapp):
    win = MainWindow()
    assert not win.overlay.isVisible()
    
    # Simulate F4 / Toggle action 1: Show
    win._toggle_overlay()
    assert win.overlay.isVisible()
    assert win.btn_overlay_toggle.isChecked()
    
    # Simulate F4 / Toggle action 2: Hide
    win._toggle_overlay()
    assert not win.overlay.isVisible()
    assert not win.btn_overlay_toggle.isChecked()
    
    # Simulate F4 / Toggle action 3: Show again
    win._toggle_overlay()
    assert win.overlay.isVisible()
    assert win.btn_overlay_toggle.isChecked()
    
    win.overlay.close()
    win.close()
