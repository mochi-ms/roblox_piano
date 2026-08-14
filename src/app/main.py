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
    if sys.platform == 'win32':
        try:
            myappid = 'roblox.piano.player.v1.0'
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)
        except Exception:
            pass

    app = QApplication(sys.argv)
    app.setApplicationName("Roblox Auto Piano Player")
    app.setOrganizationName("RobloxPiano")

    from src.utils.icon_loader import get_app_qicon
    app_icon = get_app_qicon()
    if not app_icon.isNull():
        app.setWindowIcon(app_icon)

    window = MainWindow()
    if not app_icon.isNull():
        window.setWindowIcon(app_icon)
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
