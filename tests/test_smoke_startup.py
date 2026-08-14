import os
import pytest
from PySide6.QtWidgets import QApplication
from src.app.main_window import MainWindow

@pytest.fixture(scope="session")
def qapp():
    os.environ["QT_QPA_PLATFORM"] = "offscreen"
    app = QApplication.instance()
    if app is None:
        app = QApplication([])
    return app

def test_app_startup_smoke(qapp):
    """
    Smoke test to verify that MainWindow initializes without throwing Exceptions.
    This protects against dependency order issues like accessing target_window early.
    """
    try:
        window = MainWindow()
        assert window is not None
        window.close()
    except Exception as e:
        pytest.fail(f"MainWindow initialization failed with exception: {e}")
