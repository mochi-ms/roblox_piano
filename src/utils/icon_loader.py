"""
App Icon Loader Utility
Ensures multi-size ICO resource loading across development and PyInstaller bundled environments.
"""
import os
import sys
from PySide6.QtGui import QIcon


def get_app_icon_path() -> str:
    """Returns the absolute path to the best available app icon (.ico or .png)."""
    candidates = []
    
    # 1. PyInstaller _MEIPASS bundle
    if getattr(sys, 'frozen', False) and hasattr(sys, '_MEIPASS'):
        candidates.extend([
            os.path.join(sys._MEIPASS, 'app_icon.ico'),
            os.path.join(sys._MEIPASS, 'src', 'resources', 'app_icon.ico'),
            os.path.join(sys._MEIPASS, 'src', 'resources', 'app_icon.png'),
        ])
        
    # 2. Executable directory (for deployed onedir/onefile)
    exe_dir = os.path.dirname(sys.executable)
    candidates.extend([
        os.path.join(exe_dir, 'app_icon.ico'),
        os.path.join(exe_dir, '_internal', 'app_icon.ico'),
        os.path.join(exe_dir, '_internal', 'src', 'resources', 'app_icon.ico'),
        os.path.join(exe_dir, 'src', 'resources', 'app_icon.ico'),
    ])

    # 3. Source repository root
    source_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    candidates.extend([
        os.path.join(source_root, 'src', 'resources', 'app_icon.ico'),
        os.path.join(source_root, 'app_icon.ico'),
        os.path.join(source_root, 'src', 'resources', 'app_icon.png'),
    ])

    for path in candidates:
        if path and os.path.exists(path):
            return os.path.abspath(path)
            
    return ""


def get_app_qicon() -> QIcon:
    """Creates a QIcon containing all available resolutions."""
    path = get_app_icon_path()
    if path and os.path.exists(path):
        return QIcon(path)
    return QIcon()
