import sys
import os

PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)
import tempfile
from PySide6.QtWidgets import QApplication
from PySide6.QtCore import Qt
from src.app.main_window import MainWindow
from src.utils.icon_loader import get_app_qicon, get_app_icon_path

app = QApplication.instance() or QApplication(sys.argv)

# Check Icon loading
icon_path = get_app_icon_path()
print(f"App icon resolved path: {icon_path}")
assert os.path.exists(icon_path), "Icon path must exist"
qicon = get_app_qicon()
assert not qicon.isNull(), "QIcon must be valid"

win = MainWindow()
win.tabs.setCurrentWidget(win.library_widget)
win.show()
app.processEvents()

lib = win.library_widget

# Check Sort button text (Must NOT have ▼)
print(f"Sort button text: '{lib.btn_sort.text()}'")
assert lib.btn_sort.text() == "정렬", f"Expected text '정렬', got '{lib.btn_sort.text()}'"
assert "▼" not in lib.btn_sort.text(), "Sort button text must NOT contain ▼"

# Check New button text
print(f"New button text: '{lib.btn_new.text()}'")
assert lib.btn_new.text() == "+ 새로 만들기"

# Test Resolutions
resolutions = [(1280, 720), (1366, 768), (1600, 900), (1920, 1080)]
for w, h in resolutions:
    win.resize(w, h)
    for _ in range(5):
        app.processEvents()
    addr_w = lib.address_bar.width()
    search_w = lib.search_bar.width()
    print(f"Resolution {w}x{h}: Address bar width={addr_w}, Search bar width={search_w}")
    assert addr_w > 500, f"Address bar too narrow at {w}x{h}: {addr_w}"
    assert 160 <= search_w <= 240, f"Search bar width out of bounds at {w}x{h}: {search_w}"

win.close()
print("GUI and Layout Verification 100% PASSED!")
