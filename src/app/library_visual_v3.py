"""
Roblox Piano Player — Library Visual V3
========================================

Visual-only / layout-only redesign for the existing Python V1 LibraryWidget.

Design goal
-----------
- Keep the current fast LibraryTableModel / SQLite paging path untouched.
- Keep existing LibraryManager, search debounce, navigation, shortcuts, DnD,
  favorites/recent, clipboard, inline rename and score_selected wiring untouched.
- Reuse the already-created controls from LibraryWidget and rebuild only the
  presentation shell around them.

Integration
-----------
At the *very end* of LibraryWidget._setup_ui(), after tree_view, table_view,
models, selectionModel(), status_bar and event filters already exist:

    from src.app.library_visual_v3 import apply_modern_library_shell
    apply_modern_library_shell(self)

Do not call this before the original controls are constructed.
"""

from __future__ import annotations

from typing import Callable, Optional

from PySide6.QtCore import Qt, QPoint
from PySide6.QtGui import QAction, QColor, QPainter, QPen, QBrush, QFont
from PySide6.QtWidgets import (
    QApplication,
    QAbstractItemView,
    QFrame,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLayout,
    QLineEdit,
    QMenu,
    QPushButton,
    QSizePolicy,
    QSplitter,
    QStyle,
    QStyledItemDelegate,
    QStyleOptionViewItem,
    QToolButton,
    QVBoxLayout,
    QWidget,
)


# ---------------------------------------------------------------------------
# Palette
# ---------------------------------------------------------------------------

BG = "#0B0C0E"
SURFACE = "#121316"
SURFACE_2 = "#181A1F"
SURFACE_3 = "#22252C"
HOVER = "#2B2F38"
SELECTED = "#17345B"
BORDER = "#262932"
BORDER_SOFT = "#1B1E24"

TEXT = "#F3F4F6"
TEXT_2 = "#C9CBD0"
TEXT_3 = "#9CA3AF"
TEXT_MUTED = "#6B7280"

BLUE = "#3B82F6"
BLUE_HOVER = "#4C8BF5"
BLUE_PRESSED = "#2F70DD"
GREEN = "#10B981"
DANGER = "#EF4444"


LIBRARY_V3_STYLESHEET = f"""
/* ============================================================
   Library V3 — scoped by object names to avoid global regressions
   ============================================================ */

QWidget#libraryV3Root {{
    background: {BG};
    color: {TEXT_2};
    font-family: "Malgun Gothic", "Segoe UI Variable", "Segoe UI", sans-serif;
    font-size: 13px;
}}

QFrame#librarySidebar {{
    background: {SURFACE};
    border: 1px solid {BORDER_SOFT};
    border-radius: 8px;
}}

QFrame#libraryMainCard {{
    background: {SURFACE};
    border: 1px solid {BORDER_SOFT};
    border-radius: 8px;
}}

QFrame#libraryHeaderBar {{
    background: transparent;
    border: none;
}}

QFrame#libraryFooter {{
    background: transparent;
    border: none;
    border-top: 1px solid {BORDER_SOFT};
}}

QLabel#sidebarTitle {{
    color: {TEXT_3};
    font-size: 12px;
    font-weight: 700;
    padding: 0 4px;
    letter-spacing: 0.5px;
}}

QLabel#folderSectionTitle {{
    color: {TEXT_3};
    font-size: 12px;
    font-weight: 700;
    padding: 0 4px;
    letter-spacing: 0.5px;
}}

QLabel#footerText {{
    color: {TEXT_MUTED};
    font-size: 12px;
    font-weight: 500;
}}

/* Sidebar Scope Buttons (내 라이브러리, 즐겨찾기, 최근 재생) */
QToolButton#scopeButton {{
    min-height: 34px;
    max-height: 34px;
    padding: 0 12px;
    margin: 1px 0;
    border: 1px solid transparent;
    border-radius: 8px;
    color: {TEXT_2};
    font-size: 13px;
    font-weight: 550;
    text-align: left;
    background: transparent;
}}

QToolButton#scopeButton:hover {{
    background: {HOVER};
    color: {TEXT};
    border-color: {BORDER_SOFT};
}}

QToolButton#scopeButton:checked {{
    background: {SELECTED};
    color: #73A7FF;
    border-color: #244A80;
    font-weight: 650;
}}

QTreeView#libraryTree {{
    background: transparent;
    border: none;
    outline: none;
    padding: 2px 0 0 0;
    show-decoration-selected: 1;
}}

QTreeView#libraryTree::item {{
    height: 36px;
    padding: 0 8px;
    margin: 2px 0;
    border: none;
    border-radius: 7px;
    color: {TEXT_2};
}}

QTreeView#libraryTree::item:hover {{
    background: {HOVER};
    color: {TEXT};
}}

QTreeView#libraryTree::item:selected {{
    background: {SELECTED};
    color: #73A7FF;
    font-weight: 600;
}}

QTreeView#libraryTree::branch {{
    background: transparent;
}}

QTableView#libraryTable {{
    background: transparent;
    alternate-background-color: transparent;
    border: none;
    outline: none;
    gridline-color: transparent;
    selection-background-color: {SELECTED};
    selection-color: {TEXT};
}}

QTableView#libraryTable::item {{
    min-height: 36px;
    padding: 0 8px;
    border: none;
    border-bottom: 1px solid {BORDER_SOFT};
    color: {TEXT_2};
}}

QTableView#libraryTable::item:hover {{
    background: {HOVER};
    color: {TEXT};
}}

QTableView#libraryTable::item:selected {{
    background: {SELECTED};
    color: {TEXT};
}}

QHeaderView {{
    background: transparent;
}}

QHeaderView::section {{
    background: {SURFACE_2};
    color: {TEXT_3};
    min-height: 36px;
    padding: 0 12px;
    border: none;
    border-bottom: 1px solid {BORDER};
    font-size: 12px;
    font-weight: 650;
}}

QLineEdit#librarySearch {{
    min-height: 34px;
    max-height: 34px;
    min-width: 150px;
    max-width: 240px;
    padding: 0 14px;
    color: {TEXT};
    background: {SURFACE_2};
    border: 1px solid {BORDER};
    border-radius: 6px;
    selection-background-color: {BLUE};
}}

QLineEdit#librarySearch:hover {{
    border-color: #31465F;
}}

QLineEdit#librarySearch:focus {{
    background: #101B28;
    border: 1px solid {BLUE};
}}

QFrame#locationBar,
QFrame#address_bar {{
    min-height: 34px;
    max-height: 34px;
    background: transparent;
    border: none;
}}

QPushButton#crumb,
QPushButton#crumb_btn {{
    min-height: 32px;
    padding: 0 6px;
    background: transparent;
    border: none;
    border-radius: 7px;
    color: {TEXT_3};
    font-size: 13px;
    font-weight: 550;
}}

QPushButton#crumb:hover,
QPushButton#crumb_btn:hover {{
    background: {HOVER};
    color: {TEXT};
}}

QPushButton#crumbCurrent,
QPushButton#crumb_btn_active {{
    min-height: 32px;
    padding: 0 6px;
    background: transparent;
    border: none;
    border-radius: 7px;
    color: {TEXT};
    font-size: 13px;
    font-weight: 650;
}}

QToolButton#libraryNavButton,
QPushButton#libraryIconButton {{
    min-width: 34px;
    max-width: 34px;
    min-height: 34px;
    max-height: 34px;
    padding: 0;
    background: transparent;
    color: {TEXT_3};
    border: 1px solid transparent;
    border-radius: 7px;
}}

QToolButton#libraryNavButton:hover,
QPushButton#libraryIconButton:hover {{
    background: {HOVER};
    color: {TEXT};
    border-color: {BORDER};
}}

QToolButton#libraryNavButton:disabled {{
    color: #3F4B5A;
    background: transparent;
    border-color: transparent;
}}

QPushButton#librarySecondaryButton {{
    min-height: 34px;
    padding: 0 16px;
    background: {SURFACE_2};
    color: {TEXT};
    border: 1px solid {BORDER};
    border-radius: 6px;
    font-size: 13px;
    font-weight: 650;
}}

QPushButton#librarySecondaryButton:hover {{
    background: {SURFACE_3};
    border-color: #36506E;
}}

QPushButton#librarySecondaryButton:pressed {{
    background: #0F1925;
}}

QPushButton#libraryPrimaryButton {{
    min-height: 34px;
    padding: 0 18px;
    background: {BLUE};
    color: white;
    border: 1px solid {BLUE};
    border-radius: 6px;
    font-size: 13px;
    font-weight: 700;
}}

QPushButton#libraryPrimaryButton:hover {{
    background: {BLUE_HOVER};
    border-color: {BLUE_HOVER};
}}

QPushButton#libraryPrimaryButton:pressed {{
    background: {BLUE_PRESSED};
    border-color: {BLUE_PRESSED};
}}

QPushButton#libraryMoreButton {{
    min-width: 44px;
    max-width: 44px;
    min-height: 34px;
    max-height: 34px;
    padding: 0;
    background: {SURFACE_2};
    color: {TEXT_2};
    border: 1px solid {BORDER};
    border-radius: 6px;
    font-size: 20px;
    font-weight: 700;
}}

QPushButton#libraryMoreButton:hover {{
    background: {SURFACE_3};
    color: white;
    border-color: #36506E;
}}

QMenu#libraryMenu {{
    background: #101923;
    color: {TEXT_2};
    border: 1px solid {BORDER};
    border-radius: 10px;
    padding: 6px;
}}

QMenu#libraryMenu::item {{
    min-height: 30px;
    padding: 5px 28px 5px 12px;
    border-radius: 7px;
}}

QMenu#libraryMenu::item:selected {{
    background: {HOVER};
    color: white;
}}

QMenu#libraryMenu::item:disabled {{
    color: #4B596A;
}}

QMenu#libraryMenu::separator {{
    height: 1px;
    margin: 6px 4px;
    background: {BORDER_SOFT};
}}

QSplitter#librarySplitter::handle {{
    width: 6px;
    background: transparent;
}}

QSplitter#librarySplitter::handle:hover {{
    background: #162334;
}}

QScrollBar:vertical {{
    width: 8px;
    background: transparent;
    margin: 3px 2px 3px 0;
}}

QScrollBar::handle:vertical {{
    min-height: 28px;
    background: #2B394A;
    border-radius: 4px;
}}

QScrollBar::handle:vertical:hover {{
    background: #40536B;
}}

QScrollBar::add-line:vertical,
QScrollBar::sub-line:vertical {{
    height: 0;
}}

QScrollBar:horizontal {{
    height: 8px;
    background: transparent;
}}

QScrollBar::handle:horizontal {{
    min-width: 28px;
    background: #2B394A;
    border-radius: 4px;
}}

QScrollBar::add-line:horizontal,
QScrollBar::sub-line:horizontal {{
    width: 0;
}}
"""


def get_library_stylesheet(theme_name: str = "dark") -> str:
    if theme_name.lower() != "light":
        return LIBRARY_V3_STYLESHEET

    replacements = {
        "#0B0C0E": "#F1F5F9",
        "#121316": "#FFFFFF",
        "#181A1F": "#F8FAFC",
        "#22252C": "#E2E8F0",
        "#2B2F38": "#CBD5E1",
        "#262932": "#E2E8F0",
        "#1B1E24": "#E8EDF3",
        "#17345B": "#DBEAFE",
        "#243A57": "#DBEAFE",
        "#244A80": "#93C5FD",
        "#3B82F6": "#2563EB",
        "#4C8BF5": "#1D4ED8",
        "#2F70DD": "#1E40AF",
        "#10B981": "#059669",
        "#EF4444": "#DC2626",
        "#73A7FF": "#1D4ED8",
        "#DCEBFF": "#0F172A",
        "#9CA3AF": "#475569",
        "#C9CBD0": "#1E293B",
        "#6B7280": "#64748B",
        "#0F1925": "#E2E8F0",
        "#101923": "#F8FAFC",
        "#101B28": "#F1F5F9",
        "#162334": "#CBD5E1",
        "#2B394A": "#94A3B8",
        "#31465F": "#93C5FD",
        "#36506E": "#93C5FD",
        "#3F4B5A": "#CBD5E1",
        "#40536B": "#94A3B8",
        "#4B596A": "#94A3B8",
    }
    stylesheet = LIBRARY_V3_STYLESHEET
    for dark, light in replacements.items():
        stylesheet = stylesheet.replace(dark, light)
    return stylesheet


class FormatBadgeDelegate(QStyledItemDelegate):
    def paint(self, painter: QPainter, option: QStyleOptionViewItem, index) -> None:
        if index.column() != 1:
            super().paint(painter, option, index)
            return

        text = str(index.data(Qt.DisplayRole) or "")
        item_option = QStyleOptionViewItem(option)
        self.initStyleOption(item_option, index)
        item_option.text = ""
        super().paint(painter, item_option, index)
        if not text:
            return

        selected = bool(option.state & QStyle.State_Selected)
        fill = QColor("#243A57" if selected else "#22252C")
        border = QColor("#3B82F6" if selected else "#262932")
        foreground = QColor("#DCEBFF" if selected else "#C9CBD0")
        painter.save()
        painter.setRenderHint(QPainter.Antialiasing, True)
        font = QFont("Malgun Gothic", 9)
        font.setWeight(QFont.DemiBold)
        painter.setFont(font)
        width = min(72, painter.fontMetrics().horizontalAdvance(text.upper()) + 16)
        height = 20
        x = option.rect.center().x() - width // 2
        y = option.rect.center().y() - height // 2
        painter.setPen(QPen(border, 1))
        painter.setBrush(QBrush(fill))
        painter.drawRoundedRect(x, y, width, height, 4, 4)
        painter.setPen(foreground)
        painter.drawText(x, y, width, height, Qt.AlignCenter, text.upper())
        painter.restore()



def _call_if_exists(obj, name: str, *args, **kwargs):
    fn = getattr(obj, name, None)
    if callable(fn):
        return fn(*args, **kwargs)
    return None


def _safe_connect(button, callback: Callable):
    try:
        button.clicked.connect(callback)
    except Exception:
        pass


def _detach_layout_items(layout: Optional[QLayout]) -> None:
    """
    Remove layout items recursively without deleting the widgets.

    Existing controls must survive because LibraryWidget's runtime methods still
    keep references to them (btn_cut, btn_copy, table_view, etc.).
    """
    if layout is None:
        return

    while layout.count():
        item = layout.takeAt(0)

        child_layout = item.layout()
        if child_layout is not None:
            _detach_layout_items(child_layout)
            child_layout.deleteLater()

        # Intentionally do NOT delete item.widget().
        # Re-adding it to the new shell will move it out of its previous layout.


def _hide_legacy_commands(widget) -> None:
    # Keep objects alive because _on_selection_changed() may still update them.
    names = (
        "btn_new",
        "btn_add_file",
        "btn_add_folder",
        "btn_cut",
        "btn_copy",
        "btn_paste",
        "btn_rename",
        "btn_delete",
        "btn_sort",
        "btn_refresh",
        "btn_mml",
    )
    for name in names:
        control = getattr(widget, name, None)
        if control is not None:
            control.hide()


def _selection_count(widget) -> int:
    try:
        sm = widget.table_view.selectionModel()
        return len(sm.selectedRows()) if sm is not None else 0
    except Exception:
        return 0


def _clipboard_ready(widget) -> bool:
    return getattr(widget, "clipboard_data", None) is not None


def _show_import_menu(widget, anchor: QWidget) -> None:
    menu = QMenu(anchor)
    menu.setObjectName("libraryMenu")

    act_file = menu.addAction("파일 가져오기…")
    act_file.triggered.connect(lambda: _call_if_exists(widget, "_import_files_dialog"))

    act_folder = menu.addAction("폴더 가져오기…")
    act_folder.triggered.connect(lambda: _call_if_exists(widget, "_import_folder_dialog"))

    menu.addSeparator()

    act_mml = menu.addAction("MML 가져오기…")
    act_mml.triggered.connect(lambda: _call_if_exists(widget, "_open_mml_dialog"))

    menu.exec(anchor.mapToGlobal(QPoint(0, anchor.height() + 6)))


def _show_more_menu(widget, anchor: QWidget) -> None:
    menu = QMenu(anchor)
    menu.setObjectName("libraryMenu")

    count = _selection_count(widget)

    act_open = menu.addAction("열기")
    act_open.setEnabled(count == 1)
    act_open.triggered.connect(lambda: _call_if_exists(widget, "_activate_selected"))

    menu.addSeparator()

    act_cut = menu.addAction("잘라내기")
    act_cut.setEnabled(count > 0)
    act_cut.triggered.connect(lambda: _call_if_exists(widget, "_cut_selected"))

    act_copy = menu.addAction("복사")
    act_copy.setEnabled(count > 0)
    act_copy.triggered.connect(lambda: _call_if_exists(widget, "_copy_selected"))

    act_paste = menu.addAction("붙여넣기")
    act_paste.setEnabled(_clipboard_ready(widget))
    act_paste.triggered.connect(lambda: _call_if_exists(widget, "_paste_to_current_folder"))

    menu.addSeparator()

    act_rename = menu.addAction("이름 변경")
    act_rename.setEnabled(count == 1)
    act_rename.triggered.connect(lambda: _call_if_exists(widget, "_rename_selected"))

    act_delete = menu.addAction("삭제")
    act_delete.setEnabled(count > 0)
    act_delete.triggered.connect(
        lambda: _call_if_exists(widget, "_delete_selected", permanent=False)
    )

    menu.addSeparator()

    sort_menu = menu.addMenu("정렬")
    sort_names = ("이름", "형식", "재생 시간", "BPM", "노트 수", "수정한 날짜")
    for col, label in enumerate(sort_names):
        action = sort_menu.addAction(label)
        action.triggered.connect(
            lambda _checked=False, c=col: _call_if_exists(widget, "_set_sort_column", c)
        )

    sort_menu.addSeparator()

    act_asc = sort_menu.addAction("오름차순")
    act_asc.triggered.connect(
        lambda: _call_if_exists(widget, "_set_sort_order", False)
    )

    act_desc = sort_menu.addAction("내림차순")
    act_desc.triggered.connect(
        lambda: _call_if_exists(widget, "_set_sort_order", True)
    )

    menu.addSeparator()

    act_refresh = menu.addAction("새로고침")
    act_refresh.triggered.connect(lambda: _call_if_exists(widget, "refresh_library"))

    menu.exec(anchor.mapToGlobal(QPoint(anchor.width() - menu.sizeHint().width(), anchor.height() + 6)))


def _build_sidebar(widget) -> QFrame:
    sidebar = QFrame(widget)
    sidebar.setObjectName("librarySidebar")
    sidebar.setMinimumWidth(150)
    sidebar.setMaximumWidth(205)

    layout = QVBoxLayout(sidebar)
    layout.setContentsMargins(10, 10, 10, 8)
    layout.setSpacing(6)

    title = QLabel("라이브러리", sidebar)
    title.setObjectName("sidebarTitle")
    layout.addWidget(title)

    # 1. Scope navigation buttons (내 라이브러리, 즐겨찾기, 최근 재생)
    for btn_name in ("btn_scope_library", "btn_scope_favorites", "btn_scope_recent"):
        btn = getattr(widget, btn_name, None)
        if btn is not None:
            btn.show()
            btn.setObjectName("scopeButton")
            layout.addWidget(btn)

    layout.addSpacing(10)

    # 2. Folder Section Header with '+' button
    folder_header = QWidget(sidebar)
    folder_row = QHBoxLayout(folder_header)
    folder_row.setContentsMargins(4, 0, 4, 0)
    folder_row.setSpacing(4)

    lbl_folder = QLabel("폴더", folder_header)
    lbl_folder.setObjectName("folderSectionTitle")
    folder_row.addWidget(lbl_folder)
    folder_row.addStretch(1)

    btn_add = getattr(widget, "btn_quick_new_folder", None)
    if btn_add is not None:
        btn_add.show()
        btn_add.setObjectName("libraryIconButton")
        folder_row.addWidget(btn_add)
    layout.addWidget(folder_header)

    # 3. Tree View
    tree = widget.tree_view
    tree.setObjectName("libraryTree")
    tree.setHeaderHidden(True)
    tree.setFrameShape(QFrame.NoFrame)
    tree.setIndentation(16)
    tree.setUniformRowHeights(True)
    tree.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
    tree.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)

    layout.addWidget(tree, 1)

    # 4. Optional sidebar info at bottom
    side_info = getattr(widget, "sidebar_info", None)
    if side_info is not None:
        side_info.setObjectName("footerText")
        layout.addWidget(side_info)

    return sidebar


def _build_header(widget) -> QFrame:
    header = QFrame(widget)
    header.setObjectName("libraryHeaderBar")

    row = QHBoxLayout(header)
    row.setContentsMargins(0, 0, 0, 0)
    row.setSpacing(10)

    # Navigation buttons: Back, Forward, Up
    for control_name in ("btn_back", "btn_forward", "btn_up"):
        control = getattr(widget, control_name, None)
        if control is not None:
            control.show()
            control.setObjectName("libraryNavButton")
            control.setFixedSize(32, 32)
            row.addWidget(control)

    # Existing breadcrumb widget is reused
    address = getattr(widget, "address_bar", getattr(widget, "location_bar", None))
    if address is not None:
        address.setObjectName("address_bar")
        address.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        row.addWidget(address, 1)

    # Search bar
    search = widget.search_bar
    search.setObjectName("librarySearch")
    search.setPlaceholderText("라이브러리 검색")
    search.setClearButtonEnabled(True)
    search.setMinimumWidth(260)
    search.setMaximumWidth(380)
    search.setFixedHeight(40)
    row.addWidget(search)

    # Import button (QPushButton with popup menu - no awkward toolbutton arrows)
    btn_import = QPushButton("가져오기", header)
    btn_import.setObjectName("libraryPrimaryButton")
    btn_import.setIcon(widget.style().standardIcon(QStyle.SP_DialogOpenButton))
    _safe_connect(btn_import, lambda: _show_import_menu(widget, btn_import))
    row.addWidget(btn_import)
    widget.v3_btn_import = btn_import

    # More options button (⋯)
    btn_more = QPushButton("⋯", header)
    btn_more.setObjectName("libraryMoreButton")
    btn_more.setToolTip("더 보기")
    _safe_connect(btn_more, lambda: _show_more_menu(widget, btn_more))
    row.addWidget(btn_more)
    widget.v3_btn_more = btn_more

    return header


def _configure_table(widget) -> None:
    table = widget.table_view
    table.setObjectName("libraryTable")
    table.setFrameShape(QFrame.NoFrame)
    table.setShowGrid(False)
    table.setAlternatingRowColors(False)
    table.setSelectionBehavior(QAbstractItemView.SelectRows)
    table.setSelectionMode(QAbstractItemView.ExtendedSelection)
    table.setWordWrap(False)
    table.setItemDelegateForColumn(1, FormatBadgeDelegate(table))

    vh = table.verticalHeader()
    vh.setVisible(False)
    vh.setDefaultSectionSize(36)
    vh.setMinimumSectionSize(36)

    header = table.horizontalHeader()
    header.setFixedHeight(36)
    header.setHighlightSections(False)
    header.setStretchLastSection(False)

    model = table.model()
    columns = model.columnCount() if model is not None else 0
    if columns >= 1:
        header.setSectionResizeMode(0, QHeaderView.Stretch)
    for col in range(1, columns):
        header.setSectionResizeMode(col, QHeaderView.ResizeToContents)

    if columns >= 2:
        table.setColumnWidth(1, max(table.columnWidth(1), 80))
    if columns >= 3:
        table.setColumnWidth(2, max(table.columnWidth(2), 76))
    if columns >= 4:
        table.setColumnWidth(3, max(table.columnWidth(3), 64))
    if columns >= 5:
        table.setColumnWidth(4, max(table.columnWidth(4), 90))
    if columns >= 6:
        table.setColumnWidth(5, max(table.columnWidth(5), 150))


def _build_main_card(widget) -> QFrame:
    card = QFrame(widget)
    card.setObjectName("libraryMainCard")

    layout = QVBoxLayout(card)
    layout.setContentsMargins(12, 10, 12, 0)
    layout.setSpacing(8)

    header = _build_header(widget)
    layout.addWidget(header)

    _configure_table(widget)
    layout.addWidget(widget.table_view, 1)

    return card


def _build_footer(widget) -> QFrame:
    footer = QFrame(widget)
    footer.setObjectName("libraryFooter")
    footer_layout = QHBoxLayout(footer)
    footer_layout.setContentsMargins(6, 8, 6, 0)
    footer_layout.setSpacing(10)

    # Status labels: status_left and status_right
    status_left = getattr(widget, "status_left", getattr(widget, "status_bar", None))
    if status_left is not None:
        status_left.setObjectName("footerText")
        status_left.setStyleSheet("")
        footer_layout.addWidget(status_left)

    status_right = getattr(widget, "status_right", None)
    if status_right is not None:
        status_right.setObjectName("footerText")
        status_right.setStyleSheet("")
        footer_layout.addWidget(status_right)

    footer_layout.addStretch(1)

    refresh = QPushButton(footer)
    refresh.setObjectName("libraryIconButton")
    refresh.setIcon(widget.style().standardIcon(QStyle.SP_BrowserReload))
    refresh.setToolTip("새로고침 (F5)")
    _safe_connect(refresh, lambda: _call_if_exists(widget, "refresh_library"))
    footer_layout.addWidget(refresh)
    widget.v3_btn_refresh = refresh

    return footer


def apply_modern_library_shell(widget) -> None:
    """
    Rebuild only LibraryWidget's presentation shell.

    Runtime/data guarantees:
    - does not instantiate or replace table_model
    - does not change LibraryManager
    - does not reconnect search_bar.textChanged
    - does not reconnect table double-click/context-menu/DnD events
    - does not touch score_selected
    - does not touch playback/MML parser
    """
    required = (
        "tree_view",
        "table_view",
        "search_bar",
    )
    missing = [name for name in required if not hasattr(widget, name)]
    if missing:
        raise RuntimeError(
            "Library V3 visual shell cannot attach; missing existing controls: "
            + ", ".join(missing)
        )

    widget.setObjectName("libraryV3Root")

    # Remove the old stacked navigation/command-bar layout while retaining
    # all existing controls and their signal connections.
    root = widget.layout()
    if root is None:
        root = QVBoxLayout(widget)
    else:
        _detach_layout_items(root)

    root.setContentsMargins(10, 8, 10, 8)
    root.setSpacing(8)

    _hide_legacy_commands(widget)

    splitter = QSplitter(Qt.Horizontal, widget)
    splitter.setObjectName("librarySplitter")
    splitter.setChildrenCollapsible(False)

    sidebar = _build_sidebar(widget)
    main_card = _build_main_card(widget)

    splitter.addWidget(sidebar)
    splitter.addWidget(main_card)
    splitter.setStretchFactor(0, 0)
    splitter.setStretchFactor(1, 1)
    splitter.setSizes([180, 800])

    root.addWidget(splitter, 1)
    root.addWidget(_build_footer(widget))

    widget.v3_splitter = splitter
    widget.v3_sidebar = sidebar
    widget.v3_main_card = main_card

    # Library-specific skin is scoped by ID to avoid polluting global app style
    theme_config = getattr(widget.window(), "config", None)
    theme_name = getattr(theme_config, "theme", "dark")
    widget.setStyleSheet(get_library_stylesheet(theme_name))

    # Reposition the existing empty-state overlay after the table was moved
    empty = getattr(widget, "lbl_empty", None)
    if empty is not None:
        empty.raise_()

    widget.updateGeometry()
    widget.update()
