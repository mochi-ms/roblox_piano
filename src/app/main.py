"""
Roblox Piano Player - Main Application Entrypoint
"""
import sys
import os
import ctypes

# Add project root to sys.path
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

from PySide6.QtWidgets import QApplication
from PySide6.QtCore import Qt
from src.app.main_window import MainWindow


def main():
    # Set Windows AppUserModelID for crisp taskbar icon & grouping
    try:
        myappid = 'roblox.piano.player.v1'
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)
    except Exception:
        pass

    app = QApplication(sys.argv)
    app.setApplicationName("Roblox Auto Piano Player")
    app.setOrganizationName("RobloxPiano")

    window = MainWindow()
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
