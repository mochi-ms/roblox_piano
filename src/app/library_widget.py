import json
import os
from typing import List, Optional, Set, Tuple

from PySide6.QtCore import QByteArray, QEvent, QModelIndex, QPoint, QThread, QTimer, Qt, Signal
from PySide6.QtGui import QDrag, QKeySequence, QShortcut, QStandardItem, QStandardItemModel
from PySide6.QtWidgets import (
    QApplication,
    QAbstractItemView,
    QButtonGroup,
    QFileDialog,
    QFrame,
    QHeaderView,
    QHBoxLayout,
    QInputDialog,
    QLabel,
    QLineEdit,
    QMenu,
    QMessageBox,
    QProgressDialog,
    QPushButton,
    QSizePolicy,
    QSplitter,
    QStyle,
    QTableView,
    QToolButton,
    QTreeView,
    QVBoxLayout,
    QWidget,
)
from PySide6.QtCore import QMimeData

from src.app.library_model import LibraryTableModel
from src.app.mml_dialog import MmlDialog
from src.library.manager import LibraryManager
from src.library.models import FolderItem, ScoreItem


class LibraryWidget(QWidget):
    """
    High-performance Explorer-like library UI.

    Design:
    - single clean location/search row;
    - compact command bar;
    - left navigation + folder tree;
    - QAbstractTableModel paging instead of QStandardItemModel rows;
    - debounced global search;
    - tree rebuild only when folder structure changes;
    - actual Explorer-style shortcuts / target semantics.
    """

    score_selected = Signal(ScoreItem)

    def __init__(self, manager: LibraryManager, parent=None):
        super().__init__(parent)
        self.manager = manager
        # Keep the library usable at the app's compact default window size.
        self.setMinimumWidth(0)
        self._folder_import_workers = []

        self.current_folder_id: Optional[str] = None
        self.current_scope = "folder"  # folder | favorites | recent
        self.history: List[Tuple[str, Optional[str]]] = []
        self.history_idx = -1

        self._expanded_folder_ids: Set[str] = set()
        self._tree_items: dict[str, QStandardItem] = {}

        # {"action": "copy"|"cut", "items": [("folder"|"score", obj)]}
        self.clipboard_data = None
        self._drag_start_pos: Optional[QPoint] = None

        self._search_timer = QTimer(self)
        self._search_timer.setSingleShot(True)
        self._search_timer.setInterval(250)
        self._search_timer.timeout.connect(self._apply_search)

        self._setup_ui()
        self._setup_shortcuts()
        self._rebuild_tree()
        self._navigate("folder", None)

    # ==================================================================
    # UI
    # ==================================================================
    def _setup_ui(self):
        self.setObjectName("libraryPage")
        self.setStyleSheet(
            """
            QWidget#libraryPage {
                background: #0D1117;
                color: #D0D7DE;
                font-family: "Segoe UI Variable", "Segoe UI", "Malgun Gothic";
                font-size: 13px;
            }

            QFrame#locationBar, QFrame#sidebar, QFrame#tableFrame {
                background: #10161F;
                border: 1px solid #212B38;
                border-radius: 8px;
            }

            QLineEdit#searchBox {
                background: #0D131C;
                color: #F0F6FC;
                border: 1px solid #2A3544;
                border-radius: 7px;
                padding: 7px 12px;
                min-height: 20px;
            }
            QLineEdit#searchBox:focus {
                border-color: #3B82F6;
            }

            QPushButton, QToolButton {
                background: #161D27;
                color: #D0D7DE;
                border: 1px solid #2A3544;
                border-radius: 7px;
                padding: 6px 11px;
                min-height: 20px;
            }
            QPushButton:hover, QToolButton:hover {
                background: #1D2734;
                border-color: #3A4758;
                color: #FFFFFF;
            }
            QPushButton:pressed, QToolButton:pressed {
                background: #101720;
            }
            QPushButton:disabled, QToolButton:disabled {
                background: #111720;
                color: #566171;
                border-color: #202936;
            }

            QToolButton#primaryAction, QPushButton#primaryAction {
                background: #2463D4;
                border: 1px solid #3677EA;
                color: #FFFFFF;
                font-weight: 600;
            }
            QToolButton#primaryAction:hover, QPushButton#primaryAction:hover {
                background: #2F6FDE;
            }

            QToolButton#navButton {
                background: transparent;
                border: 1px solid transparent;
                padding: 5px;
                min-width: 28px;
                max-width: 28px;
            }
            QToolButton#navButton:hover {
                background: #182231;
                border-color: #29384A;
            }

            QToolButton#scopeButton {
                text-align: left;
                background: transparent;
                border: 1px solid transparent;
                padding: 7px 10px;
                color: #AAB4C3;
            }
            QToolButton#scopeButton:hover {
                background: #151F2C;
                color: #F0F6FC;
            }
            QToolButton#scopeButton:checked {
                background: #18325B;
                border-color: #244A80;
                color: #73A7FF;
                font-weight: 600;
            }

            QLabel#sectionLabel {
                color: #748094;
                font-size: 11px;
                font-weight: 600;
                padding: 0 3px;
            }
            QLabel#statusText {
                color: #7F8C9E;
                font-size: 11px;
            }

            QTreeView {
                background: transparent;
                border: none;
                outline: none;
                padding: 2px 0;
            }
            QTreeView::item {
                min-height: 29px;
                padding: 2px 6px;
                border-radius: 5px;
                color: #B8C1CD;
            }
            QTreeView::item:hover {
                background: #151F2C;
                color: #FFFFFF;
            }
            QTreeView::item:selected {
                background: #18325B;
                color: #73A7FF;
            }

            QFrame#tableFrame {
                background: #0E141D;
            }

            QTableView {
                background: #0E141D;
                alternate-background-color: #111923;
                color: #D8DEE7;
                border: none;
                outline: none;
                gridline-color: transparent;
                selection-background-color: #173D70;
                selection-color: #FFFFFF;
            }
            QTableView::item {
                min-height: 35px;
                padding: 4px 9px;
                border-bottom: 1px solid #182230;
            }
            QTableView::item:hover {
                background: #141F2C;
            }

            QHeaderView::section {
                background: #151D28;
                color: #929EAE;
                border: none;
                border-bottom: 1px solid #273344;
                padding: 8px 10px;
                font-size: 11px;
                font-weight: 600;
            }

            QMenu {
                background: #151D28;
                color: #D8DEE7;
                border: 1px solid #303C4D;
                padding: 5px;
            }
            QMenu::item {
                padding: 7px 28px 7px 12px;
                border-radius: 5px;
            }
            QMenu::item:selected {
                background: #1E3C67;
                color: #FFFFFF;
            }
            QMenu::separator {
                height: 1px;
                background: #293545;
                margin: 5px 7px;
            }

            QSplitter::handle {
                background: transparent;
                width: 8px;
            }

            QPushButton#crumb {
                background: transparent;
                border: none;
                color: #8F9BAA;
                padding: 4px 6px;
            }
            QPushButton#crumb:hover {
                background: #172231;
                color: #DDE5EF;
            }
            QPushButton#crumbCurrent {
                background: transparent;
                border: none;
                color: #F0F6FC;
                font-weight: 600;
                padding: 4px 6px;
            }

            QLabel#emptyState {
                color: #6F7C8D;
                font-size: 13px;
            }
            """
        )

        root = QVBoxLayout(self)
        root.setContentsMargins(4, 6, 4, 4)
        root.setSpacing(8)

        # --------------------------------------------------------------
        # Location / search row
        # --------------------------------------------------------------
        top_row = QHBoxLayout()
        top_row.setSpacing(7)

        self.btn_back = self._make_nav_button(QStyle.SP_ArrowBack, "뒤로")
        self.btn_back.clicked.connect(self._go_back)
        self.btn_forward = self._make_nav_button(QStyle.SP_ArrowForward, "앞으로")
        self.btn_forward.clicked.connect(self._go_forward)
        self.btn_up = self._make_nav_button(QStyle.SP_ArrowUp, "상위 폴더")
        self.btn_up.clicked.connect(self._go_up)

        top_row.addWidget(self.btn_back)
        top_row.addWidget(self.btn_forward)
        top_row.addWidget(self.btn_up)

        self.location_bar = QFrame(self)
        self.location_bar.setObjectName("locationBar")
        self.address_bar = self.location_bar
        location_layout = QHBoxLayout(self.location_bar)
        location_layout.setContentsMargins(9, 2, 9, 2)
        location_layout.setSpacing(2)

        icon = QLabel(self.location_bar)
        icon.setPixmap(self.style().standardIcon(QStyle.SP_DirIcon).pixmap(16, 16))
        location_layout.addWidget(icon)

        self.breadcrumb_widget = QWidget(self.location_bar)
        self.breadcrumb_layout = QHBoxLayout(self.breadcrumb_widget)
        self.breadcrumb_layout.setContentsMargins(2, 0, 0, 0)
        self.breadcrumb_layout.setSpacing(1)
        location_layout.addWidget(self.breadcrumb_widget, 1)

        top_row.addWidget(self.location_bar, 1)

        self.search_bar = QLineEdit(self)
        self.search_bar.setObjectName("searchBox")
        self.search_bar.setPlaceholderText("라이브러리 검색 (Ctrl+F)")
        self.search_bar.setClearButtonEnabled(True)
        self.search_bar.setMinimumWidth(160)
        self.search_bar.setMaximumWidth(380)
        self.search_bar.textChanged.connect(self._on_search_text_changed)
        top_row.addWidget(self.search_bar)

        self.btn_mml = QPushButton("MML 가져오기", self)
        self.btn_mml.setObjectName("primaryAction")
        self.btn_mml.setToolTip("MML 코드를 붙여넣어 라이브러리에 추가")
        self.btn_mml.clicked.connect(self._open_mml_dialog)
        top_row.addWidget(self.btn_mml)

        root.addLayout(top_row)

        # --------------------------------------------------------------
        # Command bar
        # --------------------------------------------------------------
        command_row = QHBoxLayout()
        command_row.setSpacing(5)

        self.btn_new = QToolButton(self)
        self.btn_new.setObjectName("primaryAction")
        self.btn_new.setText("+ 새로 만들기")
        self.btn_new.setPopupMode(QToolButton.InstantPopup)
        new_menu = QMenu(self.btn_new)
        new_menu.addAction(
            self.style().standardIcon(QStyle.SP_FileDialogNewFolder),
            "새 폴더",
            self._create_folder,
        )
        new_menu.addAction(
            self.style().standardIcon(QStyle.SP_FileIcon),
            "파일 추가",
            self._import_files_dialog,
        )
        new_menu.addAction(
            self.style().standardIcon(QStyle.SP_DirIcon),
            "폴더 추가",
            self._import_folder_dialog,
        )
        self.btn_new.setMenu(new_menu)
        command_row.addWidget(self.btn_new)

        self.btn_cut = self._make_command_button("잘라내기", self._cut_selected)
        self.btn_copy = self._make_command_button("복사", self._copy_selected)
        self.btn_paste = self._make_command_button("붙여넣기", self._paste_to_current_folder)
        self.btn_rename = self._make_command_button("이름 변경", self._rename_selected)
        self.btn_delete = self._make_command_button(
            "삭제", lambda: self._delete_selected(permanent=False)
        )

        for btn in (self.btn_cut, self.btn_copy, self.btn_paste, self.btn_rename, self.btn_delete):
            command_row.addWidget(btn)

        command_row.addSpacing(7)

        self.btn_sort = QToolButton(self)
        self.btn_sort.setText("정렬")
        self.btn_sort.setPopupMode(QToolButton.InstantPopup)
        self.sort_menu = QMenu(self.btn_sort)
        for col, name in enumerate(LibraryTableModel.HEADERS):
            self.sort_menu.addAction(name, lambda checked=False, c=col: self._set_sort_column(c))
        self.sort_menu.addSeparator()
        self.sort_menu.addAction("오름차순", lambda: self._set_sort_order(False))
        self.sort_menu.addAction("내림차순", lambda: self._set_sort_order(True))
        self.btn_sort.setMenu(self.sort_menu)
        command_row.addWidget(self.btn_sort)

        self.btn_refresh = self._make_command_button("새로고침", self.refresh_library)
        command_row.addWidget(self.btn_refresh)

        command_row.addStretch(1)
        root.addLayout(command_row)

        # --------------------------------------------------------------
        # Main split
        # --------------------------------------------------------------
        self.splitter = QSplitter(Qt.Horizontal, self)
        root.addWidget(self.splitter, 1)

        # Left sidebar
        self.sidebar = QFrame(self)
        self.sidebar.setObjectName("sidebar")
        self.sidebar.setMinimumWidth(210)
        self.sidebar.setMaximumWidth(300)

        side_layout = QVBoxLayout(self.sidebar)
        side_layout.setContentsMargins(10, 11, 10, 10)
        side_layout.setSpacing(5)

        side_title = QLabel("라이브러리", self.sidebar)
        side_title.setObjectName("sectionLabel")
        side_layout.addWidget(side_title)

        self.scope_group = QButtonGroup(self)
        self.scope_group.setExclusive(True)

        self.btn_scope_library = self._make_scope_button(
            "내 라이브러리", QStyle.SP_DirIcon, "folder"
        )
        self.btn_scope_favorites = self._make_scope_button(
            "즐겨찾기", QStyle.SP_DialogYesButton, "favorites"
        )
        self.btn_scope_recent = self._make_scope_button(
            "최근 재생", QStyle.SP_BrowserReload, "recent"
        )

        side_layout.addWidget(self.btn_scope_library)
        side_layout.addWidget(self.btn_scope_favorites)
        side_layout.addWidget(self.btn_scope_recent)

        side_layout.addSpacing(8)
        folder_title_row = QHBoxLayout()
        lbl_folder = QLabel("폴더", self.sidebar)
        lbl_folder.setObjectName("sectionLabel")
        folder_title_row.addWidget(lbl_folder)
        folder_title_row.addStretch(1)

        self.btn_quick_new_folder = QToolButton(self.sidebar)
        self.btn_quick_new_folder.setText("+")
        self.btn_quick_new_folder.setToolTip("새 폴더")
        self.btn_quick_new_folder.clicked.connect(self._create_folder)
        self.btn_quick_new_folder.setFixedSize(26, 26)
        folder_title_row.addWidget(self.btn_quick_new_folder)
        side_layout.addLayout(folder_title_row)

        self.tree_view = QTreeView(self.sidebar)
        self.tree_view.setHeaderHidden(True)
        self.tree_view.setIndentation(16)
        self.tree_view.setUniformRowHeights(True)
        self.tree_view.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.tree_view.setContextMenuPolicy(Qt.CustomContextMenu)
        self.tree_view.customContextMenuRequested.connect(self._show_tree_context_menu)
        self.tree_view.clicked.connect(self._on_tree_clicked)
        self.tree_view.expanded.connect(self._on_tree_expanded)
        self.tree_view.collapsed.connect(self._on_tree_collapsed)
        self.tree_view.setAcceptDrops(True)

        self.tree_model = QStandardItemModel(self.tree_view)
        self.tree_view.setModel(self.tree_model)
        side_layout.addWidget(self.tree_view, 1)

        self.sidebar_info = QLabel("", self.sidebar)
        self.sidebar_info.setObjectName("statusText")
        self.sidebar_info.setWordWrap(True)
        side_layout.addWidget(self.sidebar_info)

        self.splitter.addWidget(self.sidebar)

        # Main table
        self.table_frame = QFrame(self)
        self.table_frame.setObjectName("tableFrame")
        table_layout = QVBoxLayout(self.table_frame)
        table_layout.setContentsMargins(0, 0, 0, 0)
        table_layout.setSpacing(0)

        self.table_view = QTableView(self.table_frame)
        self.table_view.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.table_view.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.table_view.setAlternatingRowColors(True)
        self.table_view.setShowGrid(False)
        self.table_view.verticalHeader().setVisible(False)
        self.table_view.verticalHeader().setDefaultSectionSize(37)
        self.table_view.setContextMenuPolicy(Qt.CustomContextMenu)
        self.table_view.customContextMenuRequested.connect(self._show_context_menu)
        self.table_view.doubleClicked.connect(self._on_table_double_clicked)
        self.table_view.setDragEnabled(True)
        self.table_view.setAcceptDrops(True)
        self.table_view.viewport().setAcceptDrops(True)
        self.table_view.setDropIndicatorShown(True)

        self.table_model = LibraryTableModel(self.manager, self.table_view, page_size=250)
        self.table_model.error.connect(
            lambda msg: QMessageBox.critical(self, "라이브러리 오류", msg)
        )
        self.table_model.folder_structure_changed.connect(self._on_folder_structure_changed)
        self.table_view.setModel(self.table_model)

        header = self.table_view.horizontalHeader()
        header.setSectionsClickable(True)
        header.sectionClicked.connect(self._header_clicked)
        header.setSortIndicatorShown(True)
        header.setSortIndicator(0, Qt.AscendingOrder)
        header.setSectionResizeMode(0, QHeaderView.Stretch)
        header.setSectionResizeMode(1, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(2, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(3, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(4, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(5, QHeaderView.ResizeToContents)
        header.setMinimumSectionSize(70)

        self.table_view.selectionModel().selectionChanged.connect(self._on_selection_changed)
        self.table_view.verticalScrollBar().valueChanged.connect(self._maybe_fetch_more)
        table_layout.addWidget(self.table_view, 1)

        self.lbl_empty = QLabel(
            "이 위치에 악보가 없습니다.\n파일을 끌어오거나 ‘파일 추가’를 선택하세요.",
            self.table_view,
        )
        self.lbl_empty.setObjectName("emptyState")
        self.lbl_empty.setAlignment(Qt.AlignCenter)
        self.lbl_empty.hide()

        self.splitter.addWidget(self.table_frame)
        self.splitter.setStretchFactor(0, 0)
        self.splitter.setStretchFactor(1, 1)
        self.splitter.setSizes([245, 1000])

        # Status
        status_row = QHBoxLayout()
        status_row.setContentsMargins(5, 0, 5, 0)

        self.status_left = QLabel("", self)
        self.status_left.setObjectName("statusText")
        self.status_right = QLabel("", self)
        self.status_right.setObjectName("statusText")

        status_row.addWidget(self.status_left)
        status_row.addStretch(1)
        status_row.addWidget(self.status_right)
        self.status_bar = self.status_left

        self.tree_view.viewport().installEventFilter(self)
        self.table_view.viewport().installEventFilter(self)

        from src.app.library_visual_v3 import apply_modern_library_shell
        apply_modern_library_shell(self)

    def _make_nav_button(self, icon_enum, tooltip):
        btn = QToolButton(self)
        btn.setObjectName("navButton")
        btn.setIcon(self.style().standardIcon(icon_enum))
        btn.setToolTip(tooltip)
        return btn

    def _make_command_button(self, text, callback):
        btn = QPushButton(text, self)
        btn.clicked.connect(callback)
        return btn

    def _make_scope_button(self, text, icon_enum, scope):
        btn = QToolButton(self.sidebar)
        btn.setObjectName("scopeButton")
        btn.setText(text)
        btn.setIcon(self.style().standardIcon(icon_enum))
        btn.setToolButtonStyle(Qt.ToolButtonTextBesideIcon)
        btn.setCheckable(True)
        btn.clicked.connect(lambda checked=False, s=scope: self._navigate(s, None))
        self.scope_group.addButton(btn)
        return btn

    # ==================================================================
    # Keyboard / focus
    # ==================================================================
    def _setup_shortcuts(self):
        QShortcut(QKeySequence(Qt.Key_F2), self.table_view, self._rename_selected)
        QShortcut(
            QKeySequence(Qt.Key_Delete),
            self.table_view,
            lambda: self._delete_selected(permanent=False),
        )
        QShortcut(
            QKeySequence(Qt.SHIFT | Qt.Key_Delete),
            self.table_view,
            lambda: self._delete_selected(permanent=True),
        )
        QShortcut(QKeySequence.SelectAll, self.table_view, self.table_view.selectAll)
        QShortcut(QKeySequence.Copy, self.table_view, self._copy_selected)
        QShortcut(QKeySequence.Cut, self.table_view, self._cut_selected)
        QShortcut(QKeySequence.Paste, self.table_view, self._paste_to_current_folder)
        QShortcut(QKeySequence.Find, self, self.search_bar.setFocus)
        QShortcut(QKeySequence(Qt.Key_F5), self, self.refresh_library)
        QShortcut(QKeySequence(Qt.ALT | Qt.Key_Left), self, self._go_back)
        QShortcut(QKeySequence(Qt.ALT | Qt.Key_Right), self, self._go_forward)
        QShortcut(QKeySequence(Qt.ALT | Qt.Key_Up), self, self._go_up)
        QShortcut(QKeySequence(Qt.Key_Return), self.table_view, self._activate_selected)
        QShortcut(QKeySequence(Qt.Key_Enter), self.table_view, self._activate_selected)
        QShortcut(QKeySequence(Qt.Key_Escape), self, self._escape_action)

    def _escape_action(self):
        if self.search_bar.text():
            self.search_bar.clear()
        else:
            self.table_view.clearSelection()

    # ==================================================================
    # Navigation
    # ==================================================================
    def _history_key(self, scope: str, folder_id: Optional[str]) -> Tuple[str, Optional[str]]:
        return scope, folder_id

    def _navigate(
        self,
        scope: str,
        folder_id: Optional[str],
        *,
        record_history: bool = True,
    ):
        target = self._history_key(scope, folder_id)
        if record_history:
            self.history = self.history[: self.history_idx + 1]
            if not self.history or self.history[-1] != target:
                self.history.append(target)
                self.history_idx = len(self.history) - 1

        self.current_scope = scope
        self.current_folder_id = folder_id if scope == "folder" else None

        self._sync_scope_buttons()
        self._sync_tree_selection()
        self._update_breadcrumb()
        self._update_nav_buttons()
        self._reload_table()

    def _sync_scope_buttons(self):
        self.btn_scope_library.setChecked(self.current_scope == "folder")
        self.btn_scope_favorites.setChecked(self.current_scope == "favorites")
        self.btn_scope_recent.setChecked(self.current_scope == "recent")

    def _go_back(self):
        if self.history_idx <= 0:
            return
        self.history_idx -= 1
        scope, folder_id = self.history[self.history_idx]
        self._navigate(scope, folder_id, record_history=False)

    def _go_forward(self):
        if self.history_idx >= len(self.history) - 1:
            return
        self.history_idx += 1
        scope, folder_id = self.history[self.history_idx]
        self._navigate(scope, folder_id, record_history=False)

    def _go_up(self):
        if self.current_scope != "folder" or not self.current_folder_id:
            return
        current = self.manager.db.get_folder(self.current_folder_id)
        self._navigate("folder", current.parent_id if current else None)

    def _update_nav_buttons(self):
        self.btn_back.setEnabled(self.history_idx > 0)
        self.btn_forward.setEnabled(self.history_idx < len(self.history) - 1)
        self.btn_up.setEnabled(self.current_scope == "folder" and self.current_folder_id is not None)

    def _update_breadcrumb(self):
        while self.breadcrumb_layout.count():
            child = self.breadcrumb_layout.takeAt(0)
            if child.widget():
                child.widget().deleteLater()

        if self.search_bar.text().strip():
            self._add_crumb("내 라이브러리", lambda: self._navigate("folder", None))
            self._add_separator()
            self._add_crumb("검색 결과", None, current=True)
            self.breadcrumb_layout.addStretch(1)
            return

        if self.current_scope == "favorites":
            self._add_crumb("즐겨찾기", None, current=True)
            self.breadcrumb_layout.addStretch(1)
            return
        if self.current_scope == "recent":
            self._add_crumb("최근 재생", None, current=True)
            self.breadcrumb_layout.addStretch(1)
            return

        self._add_crumb(
            "내 라이브러리",
            None if self.current_folder_id is None else lambda: self._navigate("folder", None),
            current=self.current_folder_id is None,
        )
        for folder in self.manager.get_folder_path_items(self.current_folder_id):
            self._add_separator()
            self._add_crumb(
                folder.name,
                None if folder.id == self.current_folder_id else lambda fid=folder.id: self._navigate("folder", fid),
                current=folder.id == self.current_folder_id,
            )
        self.breadcrumb_layout.addStretch(1)

    def _add_crumb(self, text, callback, current=False):
        btn = QPushButton(text, self.breadcrumb_widget)
        btn.setObjectName("crumbCurrent" if current else "crumb")
        btn.setCursor(Qt.PointingHandCursor)
        if callback:
            btn.clicked.connect(callback)
        else:
            btn.setEnabled(not current)
            if current:
                btn.setEnabled(True)
        self.breadcrumb_layout.addWidget(btn)

    def _add_separator(self):
        sep = QLabel("›", self.breadcrumb_widget)
        sep.setStyleSheet("color:#4E5B6C; padding:0 1px;")
        self.breadcrumb_layout.addWidget(sep)

    # ==================================================================
    # Folder tree
    # ==================================================================
    def _rebuild_tree(self):
        expanded = set(self._expanded_folder_ids)
        self.tree_model.clear()
        self._tree_items = {}

        folders = self.manager.get_all_folders()
        root = self.tree_model.invisibleRootItem()

        for folder in folders:
            item = QStandardItem(self.style().standardIcon(QStyle.SP_DirIcon), folder.name)
            item.setData(folder.id, Qt.UserRole)
            item.setEditable(False)
            self._tree_items[folder.id] = item

        for folder in folders:
            parent = self._tree_items.get(folder.parent_id)
            if parent is None:
                root.appendRow(self._tree_items[folder.id])
            else:
                parent.appendRow(self._tree_items[folder.id])

        ancestor_ids = {f.id for f in self.manager.get_folder_path_items(self.current_folder_id)}
        for folder_id, item in self._tree_items.items():
            if folder_id in expanded or folder_id in ancestor_ids:
                self.tree_view.setExpanded(item.index(), True)

        self._sync_tree_selection()
        self._update_sidebar_info()

    def _sync_tree_selection(self):
        if self.current_scope != "folder" or not self.current_folder_id:
            self.tree_view.clearSelection()
            return
        item = self._tree_items.get(self.current_folder_id)
        if item:
            self.tree_view.setCurrentIndex(item.index())

    def _on_tree_clicked(self, index: QModelIndex):
        item = self.tree_model.itemFromIndex(index)
        if item:
            folder_id = item.data(Qt.UserRole)
            if folder_id:
                self._expanded_folder_ids.add(folder_id)
                self._navigate("folder", folder_id)

    def _on_tree_expanded(self, index: QModelIndex):
        item = self.tree_model.itemFromIndex(index)
        if item and item.data(Qt.UserRole):
            self._expanded_folder_ids.add(item.data(Qt.UserRole))

    def _on_tree_collapsed(self, index: QModelIndex):
        item = self.tree_model.itemFromIndex(index)
        if item and item.data(Qt.UserRole):
            self._expanded_folder_ids.discard(item.data(Qt.UserRole))

    def _show_tree_context_menu(self, pos: QPoint):
        index = self.tree_view.indexAt(pos)
        if not index.isValid():
            return

        self.tree_view.setCurrentIndex(index)
        item = self.tree_model.itemFromIndex(index)
        folder_id = item.data(Qt.UserRole) if item else None
        if not folder_id:
            return

        menu = QMenu(self)
        menu.addAction("열기", lambda: self._navigate("folder", folder_id))
        menu.addAction("새 하위 폴더", lambda: self._create_folder(parent_id=folder_id))
        menu.addSeparator()
        menu.addAction("이름 변경", lambda: self._rename_folder_dialog(folder_id))
        menu.addAction("삭제", lambda: self._delete_folder_direct(folder_id, permanent=False))
        if self.clipboard_data:
            menu.addSeparator()
            menu.addAction("여기에 붙여넣기", lambda: self._paste_to_folder(folder_id))
        menu.exec(self.tree_view.viewport().mapToGlobal(pos))

    # ==================================================================
    # Table / search / paging / sorting
    # ==================================================================
    def _reload_table(self):
        self.table_model.set_query(
            folder_id=self.current_folder_id,
            scope=self.current_scope,
            keyword=self.search_bar.text().strip(),
            sort_column=self.table_model.sort_column,
            descending=self.table_model.descending,
        )
        self._update_empty_state()
        self._on_selection_changed()
        self._update_sidebar_info()

    def refresh_library(self, full_tree: bool = False):
        if full_tree:
            self.manager._invalidate_folder_cache()
            self._rebuild_tree()
        self._reload_table()
        self._update_breadcrumb()
        self._update_nav_buttons()

    def running_background_workers(self):
        return [worker for worker in self._folder_import_workers if worker.isRunning()]

    def cancel_background_imports(self):
        for worker in self.running_background_workers():
            worker.cancel()

    def _on_folder_structure_changed(self):
        self.refresh_library(full_tree=True)

    def _on_search_text_changed(self, _text):
        self._search_timer.start()

    def _apply_search(self):
        self._update_breadcrumb()
        self._reload_table()

    def _header_clicked(self, column: int):
        if column == self.table_model.sort_column:
            descending = not self.table_model.descending
        else:
            descending = False
        self.table_model.set_sort(column, descending)
        self.table_view.horizontalHeader().setSortIndicator(
            column, Qt.DescendingOrder if descending else Qt.AscendingOrder
        )
        self._on_selection_changed()

    def _set_sort_column(self, column: int):
        self.table_model.set_sort(column, self.table_model.descending)
        self.table_view.horizontalHeader().setSortIndicator(
            column,
            Qt.DescendingOrder if self.table_model.descending else Qt.AscendingOrder,
        )
        self._on_selection_changed()

    def _set_sort_order(self, descending: bool):
        self.table_model.set_sort(self.table_model.sort_column, descending)
        self.table_view.horizontalHeader().setSortIndicator(
            self.table_model.sort_column,
            Qt.DescendingOrder if descending else Qt.AscendingOrder,
        )
        self._on_selection_changed()

    def _maybe_fetch_more(self, value: int):
        bar = self.table_view.verticalScrollBar()
        if bar.maximum() <= 0:
            return
        if value >= bar.maximum() - max(5, bar.pageStep()):
            if self.table_model.canFetchMore():
                self.table_model.fetchMore()
                self._on_selection_changed()

    def _update_empty_state(self):
        if self.table_model.total_count == 0:
            self.lbl_empty.show()
            self.lbl_empty.raise_()
        else:
            self.lbl_empty.hide()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self.lbl_empty.setGeometry(self.table_view.viewport().rect())

    # ==================================================================
    # Selection / activation
    # ==================================================================
    def _selected_entries(self) -> list:
        rows = sorted({idx.row() for idx in self.table_view.selectionModel().selectedRows()})
        result = []
        for row in rows:
            entry = self.table_model.item_at(row)
            if entry:
                result.append(entry)
        return result

    def _on_selection_changed(self, *_args):
        selected = self._selected_entries()
        count = len(selected)

        self.btn_cut.setEnabled(count > 0)
        self.btn_copy.setEnabled(count > 0)
        self.btn_delete.setEnabled(count > 0)
        self.btn_rename.setEnabled(count == 1)
        self.btn_paste.setEnabled(bool(self.clipboard_data))

        total = self.table_model.total_count
        loaded = self.table_model.loaded_count
        self.status_left.setText(f"총 {total:,}개 항목")
        if count:
            score_count = sum(1 for kind, _ in selected if kind == "score")
            folder_count = count - score_count
            bits = [f"선택 {count}개"]
            if score_count:
                bits.append(f"악보 {score_count}")
            if folder_count:
                bits.append(f"폴더 {folder_count}")
            self.status_right.setText(" · ".join(bits))
        elif loaded < total:
            self.status_right.setText(f"{loaded:,}/{total:,}개 불러옴")
        else:
            self.status_right.setText("선택된 항목 없음")

        self._update_empty_state()

    def _on_table_double_clicked(self, index: QModelIndex):
        entry = self.table_model.item_at(index.row())
        if not entry:
            return
        kind, obj = entry
        if kind == "folder":
            self._navigate("folder", obj.id)
        else:
            self._emit_score(obj)

    def _activate_selected(self):
        selected = self._selected_entries()
        if len(selected) != 1:
            return
        kind, obj = selected[0]
        if kind == "folder":
            self._navigate("folder", obj.id)
        else:
            self._emit_score(obj)

    def _emit_score(self, score: ScoreItem):
        try:
            self.manager.mark_played(score.id)
        except Exception:
            pass
        self.score_selected.emit(score)

    # ==================================================================
    # Rename / clipboard / delete / favorite
    # ==================================================================
    def _rename_selected(self):
        rows = self.table_view.selectionModel().selectedRows()
        if len(rows) == 1:
            self.table_view.edit(self.table_model.index(rows[0].row(), 0))

    def _rename_folder_dialog(self, folder_id: str):
        folder = self.manager.db.get_folder(folder_id)
        if not folder:
            return
        new_name, ok = QInputDialog.getText(
            self, "이름 변경", "새 폴더 이름:", text=folder.name
        )
        if ok and new_name.strip() and new_name.strip() != folder.name:
            try:
                self.manager.rename_folder(folder_id, new_name.strip())
                self.refresh_library(full_tree=True)
            except Exception as exc:
                QMessageBox.critical(self, "이름 변경 오류", str(exc))

    def _copy_selected(self):
        items = self._selected_entries()
        if items:
            self.clipboard_data = {"action": "copy", "items": items}
            self._on_selection_changed()

    def _cut_selected(self):
        items = self._selected_entries()
        if items:
            self.clipboard_data = {"action": "cut", "items": items}
            self._on_selection_changed()

    def _paste_to_current_folder(self):
        target = self.current_folder_id if self.current_scope == "folder" else None
        self._paste_to_folder(target)

    def _paste_to_folder(self, target_folder_id: Optional[str]):
        if not self.clipboard_data:
            return

        action = self.clipboard_data.get("action")
        items = list(self.clipboard_data.get("items", []))
        errors = []

        for kind, obj in items:
            try:
                if kind == "score":
                    if action == "copy":
                        self.manager.copy_score(obj.id, target_folder_id)
                    else:
                        self.manager.move_score(obj.id, target_folder_id)
                elif kind == "folder":
                    if action == "copy":
                        self.manager.copy_folder(obj.id, target_folder_id)
                    else:
                        self.manager.move_folder(obj.id, target_folder_id)
            except Exception as exc:
                errors.append(f"{getattr(obj, 'title', getattr(obj, 'name', obj))}: {exc}")

        if action == "cut" and not errors:
            self.clipboard_data = None

        self.refresh_library(full_tree=True)
        if errors:
            QMessageBox.warning(
                self,
                "일부 작업 실패",
                "일부 항목을 처리하지 못했습니다.\n\n" + "\n".join(errors[:10]),
            )

    def _delete_selected(self, permanent: bool = False):
        items = self._selected_entries()
        if not items:
            return

        count = len(items)
        if permanent:
            message = (
                f"선택한 {count}개 항목을 완전히 삭제하시겠습니까?\n"
                "이 작업은 휴지통을 거치지 않으며 복구할 수 없습니다."
            )
        else:
            message = f"선택한 {count}개 항목을 Windows 휴지통으로 이동하시겠습니까?"

        answer = QMessageBox.question(
            self,
            "삭제 확인",
            message,
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if answer != QMessageBox.Yes:
            return

        errors = []
        folder_changed = False
        for kind, obj in items:
            try:
                if kind == "folder":
                    self.manager.delete_folder(obj.id, permanent=permanent)
                    folder_changed = True
                else:
                    self.manager.delete_score(obj.id, permanent=permanent)
            except Exception as exc:
                errors.append(str(exc))

        self.refresh_library(full_tree=folder_changed)
        if errors:
            QMessageBox.warning(self, "일부 삭제 실패", "\n".join(errors[:10]))

    def _delete_folder_direct(self, folder_id: str, permanent: bool = False):
        folder = self.manager.db.get_folder(folder_id)
        if not folder:
            return
        answer = QMessageBox.question(
            self,
            "폴더 삭제",
            f"‘{folder.name}’ 폴더와 그 안의 항목을 휴지통으로 이동하시겠습니까?",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if answer != QMessageBox.Yes:
            return
        try:
            self.manager.delete_folder(folder_id, permanent=permanent)
            if self.current_folder_id == folder_id:
                self._navigate("folder", folder.parent_id)
            self.refresh_library(full_tree=True)
        except Exception as exc:
            QMessageBox.critical(self, "삭제 오류", str(exc))

    def _toggle_selected_favorite(self):
        selected = self._selected_entries()
        score_entries = [(kind, obj) for kind, obj in selected if kind == "score"]
        if len(score_entries) != 1:
            return
        try:
            self.manager.toggle_favorite(score_entries[0][1].id)
            self._reload_table()
        except Exception as exc:
            QMessageBox.critical(self, "즐겨찾기 오류", str(exc))

    # ==================================================================
    # Context menus
    # ==================================================================
    def _show_context_menu(self, pos: QPoint):
        index = self.table_view.indexAt(pos)

        if index.isValid():
            row = index.row()
            selected_rows = {idx.row() for idx in self.table_view.selectionModel().selectedRows()}
            if row not in selected_rows:
                self.table_view.clearSelection()
                self.table_view.selectRow(row)

        entries = self._selected_entries()
        menu = QMenu(self)

        if entries:
            if len(entries) == 1:
                kind, obj = entries[0]
                if kind == "score":
                    menu.addAction("열기", lambda s=obj: self._emit_score(s))
                    menu.addAction(
                        "즐겨찾기 해제" if obj.favorite else "즐겨찾기 추가",
                        self._toggle_selected_favorite,
                    )
                else:
                    menu.addAction("열기", lambda f=obj: self._navigate("folder", f.id))
                menu.addSeparator()

            menu.addAction("잘라내기", self._cut_selected)
            menu.addAction("복사", self._copy_selected)
            if len(entries) == 1:
                menu.addAction("이름 변경", self._rename_selected)
            menu.addAction("삭제", lambda: self._delete_selected(permanent=False))

            permanent = menu.addAction("영구 삭제")
            permanent.triggered.connect(lambda: self._delete_selected(permanent=True))
            menu.addSeparator()

        if self.clipboard_data:
            menu.addAction("여기에 붙여넣기", self._paste_to_current_folder)
            menu.addSeparator()

        menu.addAction("새 폴더", self._create_folder)
        menu.addAction("파일 추가", self._import_files_dialog)
        menu.addAction("새로고침", self.refresh_library)

        menu.exec(self.table_view.viewport().mapToGlobal(pos))

    # ==================================================================
    # Creation / import / MML
    # ==================================================================
    def _create_folder(self, checked=False, parent_id: Optional[str] = None):
        if isinstance(checked, str):
            checked = False
        target_parent = (
            parent_id
            if parent_id is not None
            else (self.current_folder_id if self.current_scope == "folder" else None)
        )
        name, ok = QInputDialog.getText(self, "새 폴더", "폴더 이름:")
        if not ok or not name.strip():
            return
        try:
            created = self.manager.create_folder(name.strip(), target_parent)
            self._expanded_folder_ids.add(target_parent) if target_parent else None
            self.refresh_library(full_tree=True)
            if self.current_scope == "folder" and self.current_folder_id == target_parent:
                item = self._tree_items.get(created.id)
                if item:
                    self.tree_view.scrollTo(item.index())
        except Exception as exc:
            QMessageBox.critical(self, "폴더 생성 오류", str(exc))

    def _import_files_dialog(self):
        files, _ = QFileDialog.getOpenFileNames(
            self,
            "악보 파일 가져오기",
            "",
            "지원 파일 (*.mid *.midi *.musicxml *.xml *.mxl *.pdf *.png *.jpg *.jpeg *.txt *.mml);;모든 파일 (*.*)",
        )
        if not files:
            return

        target = self.current_folder_id if self.current_scope == "folder" else None
        errors = []
        for path in files:
            try:
                self.manager.import_external_file(path, folder_id=target)
            except Exception as exc:
                errors.append(f"{os.path.basename(path)}: {exc}")

        self.refresh_library()
        if errors:
            QMessageBox.warning(self, "일부 파일 가져오기 실패", "\n".join(errors[:10]))

    def _import_folder_dialog(self):
        path = QFileDialog.getExistingDirectory(self, "가져올 Windows 폴더 선택")
        if not path:
            return
        target = self.current_folder_id if self.current_scope == "folder" else None
        self._start_folder_import(path, target_parent_folder_id=target)

    def _open_mml_dialog(self):
        target = self.current_folder_id if self.current_scope == "folder" else None
        dialog = MmlDialog(self.manager, target, self)
        if dialog.exec():
            self.refresh_library()
            created = dialog.get_created_score_item()
            if created and dialog.should_play():
                self._emit_score(created)

    def _start_folder_import(
        self,
        source_folder_path: str,
        target_parent_folder_id: Optional[str],
    ):
        progress = QProgressDialog("폴더와 악보를 가져오는 중...", "취소", 0, 100, self)
        progress.setWindowModality(Qt.WindowModal)
        progress.setAutoClose(True)
        progress.setMinimumDuration(0)
        progress.show()

        worker = FolderImportWorker(
            self.manager, source_folder_path, target_parent_folder_id, self
        )
        self._folder_import_workers.append(worker)

        def release_worker():
            try:
                self._folder_import_workers.remove(worker)
            except ValueError:
                pass

        def on_progress(current, total, name):
            progress.setMaximum(max(1, total))
            progress.setValue(current)
            progress.setLabelText(f"가져오는 중 ({current}/{total})\n{name}")

        def on_finished(summary):
            progress.close()
            self.refresh_library(full_tree=True)
            message = (
                "폴더 가져오기 완료\n\n"
                f"폴더: {summary.get('imported_folders', 0)}개\n"
                f"악보: {summary.get('imported_scores', 0)}개\n"
                f"건너뜀: {summary.get('skipped', 0)}개\n"
                f"실패: {summary.get('failed', 0)}개"
            )
            QMessageBox.information(self, "가져오기 완료", message)

        def on_error(message):
            progress.close()
            self.refresh_library(full_tree=True)
            QMessageBox.critical(self, "가져오기 오류", message)

        progress.canceled.connect(worker.cancel)
        worker.sig_progress.connect(on_progress)
        worker.sig_finished.connect(on_finished)
        worker.sig_error.connect(on_error)
        worker.finished.connect(release_worker)
        worker.start()

    # ==================================================================
    # Drag & drop
    # ==================================================================
    def _start_internal_drag(self):
        entries = self._selected_entries()
        if not entries:
            return

        payload = {
            "scores": [obj.id for kind, obj in entries if kind == "score"],
            "folders": [obj.id for kind, obj in entries if kind == "folder"],
        }
        if not payload["scores"] and not payload["folders"]:
            return

        mime = QMimeData()
        mime.setData(
            "application/x-roblox-piano-items",
            QByteArray(json.dumps(payload).encode("utf-8")),
        )
        drag = QDrag(self.table_view)
        drag.setMimeData(mime)
        drag.exec(Qt.MoveAction)

    def _get_drop_target_folder_id(self, source, pos: QPoint) -> Optional[str]:
        if source is self.tree_view.viewport():
            index = self.tree_view.indexAt(pos)
            if index.isValid():
                item = self.tree_model.itemFromIndex(index)
                return item.data(Qt.UserRole) if item else None
            return None

        if source is self.table_view.viewport():
            index = self.table_view.indexAt(pos)
            if index.isValid():
                entry = self.table_model.item_at(index.row())
                if entry and entry[0] == "folder":
                    return entry[1].id
            return self.current_folder_id if self.current_scope == "folder" else None

        return self.current_folder_id if self.current_scope == "folder" else None

    def eventFilter(self, source, event):
        if source not in (self.table_view.viewport(), self.tree_view.viewport()):
            return super().eventFilter(source, event)

        if source is self.table_view.viewport():
            if event.type() == QEvent.Type.MouseButtonPress and event.button() == Qt.LeftButton:
                self._drag_start_pos = event.pos()
            elif event.type() == QEvent.Type.MouseMove:
                if (
                    event.buttons() & Qt.LeftButton
                    and self._drag_start_pos is not None
                    and (event.pos() - self._drag_start_pos).manhattanLength()
                    >= QApplication.startDragDistance()
                ):
                    self._start_internal_drag()
                    self._drag_start_pos = None
                    return True

        if event.type() in (QEvent.Type.DragEnter, QEvent.Type.DragMove):
            mime = event.mimeData()
            if mime.hasUrls() or mime.hasFormat("application/x-roblox-piano-items"):
                event.acceptProposedAction()
                return True

        if event.type() == QEvent.Type.Drop:
            pos = event.position().toPoint()
            target_folder_id = self._get_drop_target_folder_id(source, pos)
            mime = event.mimeData()

            if mime.hasFormat("application/x-roblox-piano-items"):
                try:
                    raw = bytes(mime.data("application/x-roblox-piano-items")).decode("utf-8")
                    payload = json.loads(raw)
                    folders = payload.get("folders", [])
                    scores = payload.get("scores", [])

                    for folder_id in folders:
                        if folder_id == target_folder_id or self.manager.is_descendant(
                            folder_id, target_folder_id
                        ):
                            QMessageBox.warning(
                                self,
                                "이동 불가",
                                "자기 자신이나 하위 폴더로는 이동할 수 없습니다.",
                            )
                            return True

                    for score_id in scores:
                        self.manager.move_score(score_id, target_folder_id)
                    for folder_id in folders:
                        self.manager.move_folder(folder_id, target_folder_id)

                    self.refresh_library(full_tree=bool(folders))
                except Exception as exc:
                    QMessageBox.critical(self, "이동 오류", str(exc))

                event.acceptProposedAction()
                return True

            if mime.hasUrls():
                folders = []
                files = []
                for url in mime.urls():
                    if not url.isLocalFile():
                        continue
                    path = url.toLocalFile()
                    if os.path.isdir(path):
                        folders.append(path)
                    else:
                        files.append(path)

                for file_path in files:
                    try:
                        self.manager.import_external_file(
                            file_path, folder_id=target_folder_id
                        )
                    except Exception as exc:
                        print(f"Failed to import dropped file {file_path}: {exc}")

                # Run folder imports one by one. Their workers update UI when done.
                for folder_path in folders:
                    self._start_folder_import(
                        folder_path, target_parent_folder_id=target_folder_id
                    )

                if files:
                    self.refresh_library()
                event.acceptProposedAction()
                return True

        return super().eventFilter(source, event)

    # ==================================================================
    # Misc
    # ==================================================================
    def _update_sidebar_info(self):
        folder_count = len(self.manager.get_all_folders())
        self.sidebar_info.setText(
            f"폴더 {folder_count:,}개\n"
            f"현재 보기 {self.table_model.total_count:,}개 항목"
        )


class FolderImportWorker(QThread):
    sig_progress = Signal(int, int, str)
    sig_finished = Signal(dict)
    sig_error = Signal(str)

    def __init__(
        self,
        manager: LibraryManager,
        source_folder_path: str,
        target_parent_folder_id: Optional[str] = None,
        parent=None,
    ):
        super().__init__(parent)
        self.manager = manager
        self.source_folder_path = source_folder_path
        self.target_parent_folder_id = target_parent_folder_id
        self._is_cancelled = False

    def cancel(self):
        self._is_cancelled = True

    def run(self):
        try:
            summary = self.manager.import_folder_recursive(
                self.source_folder_path,
                self.target_parent_folder_id,
                progress_callback=self._on_progress,
                cancel_check=lambda: self._is_cancelled,
            )
            self.sig_finished.emit(summary)
        except Exception as exc:
            self.sig_error.emit(str(exc))

    def _on_progress(self, current: int, total: int, name: str):
        self.sig_progress.emit(current, total, name)
