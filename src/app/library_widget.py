import os
import json
import datetime
import subprocess
from typing import Optional, List, Tuple, Set

from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLineEdit,
    QTreeView, QTableView, QHeaderView, QMenu, QInputDialog, QMessageBox,
    QLabel, QSplitter, QAbstractItemView, QFrame, QToolButton, QFileDialog,
    QStyledItemDelegate, QStyle, QProgressDialog, QApplication, QScrollArea
)
from PySide6.QtCore import Qt, QPoint, Signal, QModelIndex, QEvent, QThread, QMimeData, QByteArray
from PySide6.QtGui import (
    QStandardItemModel, QStandardItem, QIcon, QAction, QKeySequence,
    QShortcut, QFontMetrics, QDrag, QPainter, QColor, QCursor
)

from src.library.manager import LibraryManager
from src.library.models import ScoreItem, FolderItem
from src.app.mml_dialog import MmlDialog
from src.music.timeline import MusicTimeline


class LibraryWidget(QWidget):
    score_selected = Signal(ScoreItem)

    def __init__(self, manager: LibraryManager, parent=None):
        super().__init__(parent)
        self.manager = manager
        
        # Navigation History
        self.history: List[Optional[str]] = []
        self.history_idx = -1
        self.current_folder_id: Optional[str] = None
        
        # Expanded Tree Folder IDs persistence
        self._expanded_folder_ids: Set[Optional[str]] = {None}
        
        # Internal clipboard for copy / cut
        # Format: {"action": "copy" | "cut", "items": [("folder"|"score", obj)]}
        self.clipboard_data = None
        
        self._is_internal_editing = False
        self._drag_start_pos = None
        self._sort_column = 0
        self._sort_order = Qt.AscendingOrder

        self._setup_ui()
        self._setup_shortcuts()
        self._navigate(None)

    def _setup_ui(self):
        self.setStyleSheet("""
            QWidget {
                background-color: #0D1117;
                color: #C9D1D9;
                font-family: 'Segoe UI Variable', 'Segoe UI', -apple-system, BlinkMacSystemFont, sans-serif;
                font-size: 13px;
            }
            QSplitter::handle {
                background-color: #21262D;
                width: 1px;
            }
            QTreeView {
                background-color: #0D1117;
                border: 1px solid #21262D;
                border-radius: 6px;
                outline: none;
                padding: 4px;
            }
            QTreeView::item {
                padding: 4px 6px;
                min-height: 28px;
                border-radius: 4px;
                color: #C9D1D9;
            }
            QTreeView::item:hover {
                background-color: #161B22;
                color: #F0F6FC;
            }
            QTreeView::item:selected {
                background-color: #1F2A38;
                color: #FFFFFF;
                border-left: 2px solid #4C82F7;
            }
            QTableView {
                background-color: #0D1117;
                border: 1px solid #21262D;
                border-radius: 6px;
                outline: none;
                alternate-background-color: #12161D;
                selection-background-color: #1C2B42;
                selection-color: #FFFFFF;
                gridline-color: transparent;
            }
            QTableView::item {
                padding: 4px 8px;
                min-height: 32px;
                border-radius: 4px;
            }
            QTableView::item:hover {
                background-color: #161B22;
            }
            QTableView::item:selected {
                background-color: #1C2B42;
                color: #FFFFFF;
            }
            QHeaderView::section {
                background-color: #161B22;
                color: #8B949E;
                padding: 6px 10px;
                border: none;
                border-bottom: 1px solid #21262D;
                font-weight: 600;
                font-size: 12px;
            }
            QPushButton, QToolButton {
                background-color: #21262D;
                color: #C9D1D9;
                border: 1px solid #30363D;
                border-radius: 6px;
                padding: 5px 11px;
                font-size: 13px;
                font-weight: 500;
            }
            QPushButton:hover, QToolButton:hover {
                background-color: #30363D;
                border-color: #8B949E;
                color: #F0F6FC;
            }
            QPushButton:pressed, QToolButton:pressed {
                background-color: #161B22;
            }
            QPushButton:disabled, QToolButton:disabled {
                background-color: #12151A;
                border-color: #21262D;
                color: #484F58;
            }
            #cmd_btn {
                background-color: transparent;
                border: 1px solid transparent;
                color: #C9D1D9;
                padding: 5px 9px;
                border-radius: 6px;
            }
            #cmd_btn:hover {
                background-color: #21262D;
                border-color: #30363D;
                color: #F0F6FC;
            }
            #cmd_btn:pressed {
                background-color: #161B22;
            }
            #cmd_btn:disabled {
                background-color: transparent;
                border-color: transparent;
                color: #484F58;
            }
            #address_bar {
                background-color: #161B22;
                border: 1px solid #30363D;
                border-radius: 6px;
            }
            #address_bar:focus-within {
                border: 1px solid #4C82F7;
            }
            QLineEdit {
                background-color: #161B22;
                color: #F0F6FC;
                border: 1px solid #30363D;
                border-radius: 6px;
                padding: 5px 10px;
                font-size: 13px;
            }
            QLineEdit:focus {
                border: 1px solid #4C82F7;
                background-color: #0D1117;
            }
            #crumb_btn {
                background-color: transparent;
                border: none;
                color: #8B949E;
                font-size: 13px;
                font-weight: 500;
                padding: 3px 6px;
                border-radius: 4px;
            }
            #crumb_btn:hover {
                background-color: #21262D;
                color: #58A6FF;
            }
            #crumb_btn_active {
                background-color: transparent;
                border: none;
                color: #F0F6FC;
                font-size: 13px;
                font-weight: 600;
                padding: 3px 6px;
            }
            #statusBar {
                background-color: #161B22;
                border-top: 1px solid #21262D;
                color: #8B949E;
                font-size: 12px;
                padding: 4px 12px;
                border-radius: 4px;
            }
            QMenu {
                background-color: #161B22;
                border: 1px solid #30363D;
                border-radius: 6px;
                padding: 4px 0;
            }
            QMenu::item {
                padding: 6px 24px 6px 20px;
                color: #C9D1D9;
                font-size: 13px;
            }
            QMenu::item:selected {
                background-color: #1F3A60;
                color: #FFFFFF;
            }
            QMenu::separator {
                height: 1px;
                background-color: #21262D;
                margin: 4px 8px;
            }
        """)
        
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(12, 10, 12, 8)
        main_layout.setSpacing(6)
        
        # =====================================================================
        # ROW 1: Navigation / Location Bar & Search Bar (Windows 11 Explorer Style)
        # =====================================================================
        nav_row = QHBoxLayout()
        nav_row.setSpacing(6)
        
        self.btn_back = QToolButton(self)
        self.btn_back.setIcon(self.style().standardIcon(QStyle.SP_ArrowBack))
        self.btn_back.setToolTip("뒤로 가기 (Alt+Left)")
        self.btn_back.clicked.connect(self._go_back)
        
        self.btn_forward = QToolButton(self)
        self.btn_forward.setIcon(self.style().standardIcon(QStyle.SP_ArrowForward))
        self.btn_forward.setToolTip("앞으로 가기 (Alt+Right)")
        self.btn_forward.clicked.connect(self._go_forward)
        
        self.btn_up = QToolButton(self)
        self.btn_up.setIcon(self.style().standardIcon(QStyle.SP_ArrowUp))
        self.btn_up.setToolTip("상위 폴더로 (Alt+Up)")
        self.btn_up.clicked.connect(self._go_up)
        
        nav_row.addWidget(self.btn_back)
        nav_row.addWidget(self.btn_forward)
        nav_row.addWidget(self.btn_up)
        
        # Address Bar container with Clickable Breadcrumb
        self.address_bar = QFrame(self)
        self.address_bar.setObjectName("address_bar")
        addr_layout = QHBoxLayout(self.address_bar)
        addr_layout.setContentsMargins(6, 2, 6, 2)
        addr_layout.setSpacing(2)
        
        # Icon inside Address Bar
        lbl_loc_icon = QLabel(self.address_bar)
        lbl_loc_icon.setPixmap(self.style().standardIcon(QStyle.SP_DirIcon).pixmap(16, 16))
        addr_layout.addWidget(lbl_loc_icon)
        
        # Scrollable Breadcrumbs Container
        self.breadcrumb_container = QWidget(self.address_bar)
        self.breadcrumb_layout = QHBoxLayout(self.breadcrumb_container)
        self.breadcrumb_layout.setContentsMargins(2, 0, 2, 0)
        self.breadcrumb_layout.setSpacing(2)
        addr_layout.addWidget(self.breadcrumb_container, 1)
        
        nav_row.addWidget(self.address_bar, 1)
        
        # Search Box
        self.search_bar = QLineEdit(self)
        self.search_bar.setPlaceholderText("라이브러리 검색 (Ctrl+F)...")
        self.search_bar.setClearButtonEnabled(True)
        self.search_bar.textChanged.connect(self._on_search)
        self.search_bar.setMinimumWidth(180)
        self.search_bar.setMaximumWidth(260)
        nav_row.addWidget(self.search_bar)
        
        main_layout.addLayout(nav_row)
        
        # =====================================================================
        # ROW 2: Command Bar (Windows 11 Explorer Command Ribbon)
        # =====================================================================
        cmd_bar = QHBoxLayout()
        cmd_bar.setSpacing(4)
        
        # + 새로 만들기 Dropdown
        self.btn_new = QToolButton(self)
        self.btn_new.setText("+ 새로 만들기")
        self.btn_new.setPopupMode(QToolButton.InstantPopup)
        menu_new = QMenu(self.btn_new)
        act_new_folder = menu_new.addAction(self.style().standardIcon(QStyle.SP_FileDialogNewFolder), "새 폴더")
        act_new_folder.triggered.connect(self._create_folder)
        act_add_file = menu_new.addAction(self.style().standardIcon(QStyle.SP_FileIcon), "파일 추가...")
        act_add_file.triggered.connect(self._import_files_dialog)
        act_add_folder = menu_new.addAction(self.style().standardIcon(QStyle.SP_DirIcon), "폴더 추가...")
        act_add_folder.triggered.connect(self._import_folder_dialog)
        self.btn_new.setMenu(menu_new)
        cmd_bar.addWidget(self.btn_new)

        self.btn_add_file = QPushButton("파일 추가", self)
        self.btn_add_file.setObjectName("cmd_btn")
        self.btn_add_file.clicked.connect(self._import_files_dialog)
        cmd_bar.addWidget(self.btn_add_file)
        
        self.btn_add_folder = QPushButton("폴더 추가", self)
        self.btn_add_folder.setObjectName("cmd_btn")
        self.btn_add_folder.setToolTip("Windows 폴더를 통째로 가져옵니다")
        self.btn_add_folder.clicked.connect(self._import_folder_dialog)
        cmd_bar.addWidget(self.btn_add_folder)
        
        # Separator 1
        sep1 = QFrame(self)
        sep1.setFrameShape(QFrame.VLine)
        sep1.setStyleSheet("color: #21262D; margin: 4px 4px;")
        cmd_bar.addWidget(sep1)
        
        # Edit Operations: Cut, Copy, Paste, Rename, Delete
        self.btn_cut = QPushButton("잘라내기", self)
        self.btn_cut.setObjectName("cmd_btn")
        self.btn_cut.setToolTip("잘라내기 (Ctrl+X)")
        self.btn_cut.clicked.connect(self._cut_selected)
        cmd_bar.addWidget(self.btn_cut)
        
        self.btn_copy = QPushButton("복사", self)
        self.btn_copy.setObjectName("cmd_btn")
        self.btn_copy.setToolTip("복사 (Ctrl+C)")
        self.btn_copy.clicked.connect(self._copy_selected)
        cmd_bar.addWidget(self.btn_copy)
        
        self.btn_paste = QPushButton("붙여넣기", self)
        self.btn_paste.setObjectName("cmd_btn")
        self.btn_paste.setToolTip("붙여넣기 (Ctrl+V)")
        self.btn_paste.clicked.connect(self._paste_to_current_folder)
        cmd_bar.addWidget(self.btn_paste)
        
        self.btn_rename = QPushButton("이름 변경", self)
        self.btn_rename.setObjectName("cmd_btn")
        self.btn_rename.setToolTip("이름 변경 (F2)")
        self.btn_rename.clicked.connect(self._rename_selected)
        cmd_bar.addWidget(self.btn_rename)
        
        self.btn_delete = QPushButton("삭제", self)
        self.btn_delete.setObjectName("cmd_btn")
        self.btn_delete.setToolTip("휴지통으로 삭제 (Del) / 영구 삭제 (Shift+Del)")
        self.btn_delete.clicked.connect(lambda: self._delete_selected(permanent=False))
        cmd_bar.addWidget(self.btn_delete)
        
        # Separator 2
        sep2 = QFrame(self)
        sep2.setFrameShape(QFrame.VLine)
        sep2.setStyleSheet("color: #21262D; margin: 4px 4px;")
        cmd_bar.addWidget(sep2)
        
        # Sort Dropdown
        self.btn_sort = QToolButton(self)
        self.btn_sort.setText("정렬 ▼")
        self.btn_sort.setObjectName("cmd_btn")
        self.btn_sort.setPopupMode(QToolButton.InstantPopup)
        menu_sort = QMenu(self.btn_sort)
        for col_idx, col_name in enumerate(["이름", "형식", "재생 시간", "BPM", "노트 수", "수정한 날짜"]):
            act = menu_sort.addAction(col_name)
            act.triggered.connect(lambda _, c=col_idx: self._set_sort_column(c))
        menu_sort.addSeparator()
        act_asc = menu_sort.addAction("오름차순")
        act_asc.triggered.connect(lambda: self._set_sort_order(Qt.AscendingOrder))
        act_desc = menu_sort.addAction("내림차순")
        act_desc.triggered.connect(lambda: self._set_sort_order(Qt.DescendingOrder))
        self.btn_sort.setMenu(menu_sort)
        cmd_bar.addWidget(self.btn_sort)
        
        cmd_bar.addStretch(1)
        
        # MML Import Button
        btn_mml = QPushButton("MML 가져오기", self)
        btn_mml.setStyleSheet("background-color: #2D4C7C; color: #FFFFFF; border: 1px solid #4C82F7; font-weight: 600; padding: 5px 12px;")
        btn_mml.clicked.connect(self._open_mml_dialog)
        cmd_bar.addWidget(btn_mml)
        
        main_layout.addLayout(cmd_bar)
        
        # =====================================================================
        # 3. Main Splitter (Left Sidebar Tree + Right Details Table)
        # =====================================================================
        self.splitter = QSplitter(Qt.Horizontal, self)
        main_layout.addWidget(self.splitter, 1)
        
        # Left Tree View (Navigation Pane)
        self.tree_view = QTreeView(self)
        self.tree_view.setHeaderHidden(True)
        self.tree_view.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.tree_view.setIndentation(18)
        self.tree_view.setUniformRowHeights(True)
        self.tree_view.clicked.connect(self._on_tree_clicked)
        self.tree_view.expanded.connect(self._on_tree_expanded)
        self.tree_view.collapsed.connect(self._on_tree_collapsed)
        
        self.tree_view.setAcceptDrops(True)
        self.splitter.addWidget(self.tree_view)
        
        # Right Table View Container
        table_container = QWidget(self)
        table_layout = QVBoxLayout(table_container)
        table_layout.setContentsMargins(0, 0, 0, 0)
        table_layout.setSpacing(0)

        self.table_view = QTableView(self)
        self.table_view.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.table_view.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.table_view.verticalHeader().setVisible(False)
        self.table_view.verticalHeader().setDefaultSectionSize(36)
        self.table_view.setShowGrid(False)
        self.table_view.setAlternatingRowColors(True)
        self.table_view.doubleClicked.connect(self._on_table_double_clicked)
        self.table_view.setContextMenuPolicy(Qt.CustomContextMenu)
        self.table_view.customContextMenuRequested.connect(self._show_context_menu)
        
        self.table_view.setAcceptDrops(True)
        self.table_view.setDragEnabled(True)
        self.table_view.setDropIndicatorShown(True)
        self.table_view.viewport().setAcceptDrops(True)
        
        table_layout.addWidget(self.table_view, 1)

        # Empty state label overlay
        self.lbl_empty = QLabel("이 폴더에 악보가 없습니다.\n파일을 끌어오거나 '파일 추가'를 선택하세요.", self.table_view)
        self.lbl_empty.setAlignment(Qt.AlignCenter)
        self.lbl_empty.setStyleSheet("color: #758195; font-size: 13px; line-height: 1.5;")
        self.lbl_empty.hide()

        self.splitter.addWidget(table_container)
        self.splitter.setSizes([240, 720])

        self.tree_model = QStandardItemModel(self)
        self.tree_view.setModel(self.tree_model)
        
        self.table_model = QStandardItemModel(self)
        self.table_model.setHorizontalHeaderLabels(["이름", "형식", "재생 시간", "BPM", "노트 수", "수정한 날짜"])
        self.table_model.itemChanged.connect(self._on_item_edited)
        self.table_view.setModel(self.table_model)
        
        # Header configuration
        header = self.table_view.horizontalHeader()
        header.setFixedHeight(34)
        header.setSectionResizeMode(0, QHeaderView.Stretch)
        header.setSectionResizeMode(1, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(2, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(3, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(4, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(5, QHeaderView.ResizeToContents)

        self.table_view.selectionModel().selectionChanged.connect(self._on_selection_changed)

        # Bottom Status Bar
        self.status_bar = QLabel(self)
        self.status_bar.setObjectName("statusBar")
        main_layout.addWidget(self.status_bar)

        # Install event filters after all widgets are constructed
        self.tree_view.viewport().installEventFilter(self)
        self.table_view.viewport().installEventFilter(self)

    def _setup_shortcuts(self):
        QShortcut(QKeySequence(Qt.Key_F2), self.table_view, self._rename_selected)
        QShortcut(QKeySequence(Qt.Key_Delete), self.table_view, lambda: self._delete_selected(permanent=False))
        QShortcut(QKeySequence(Qt.SHIFT | Qt.Key_Delete), self.table_view, lambda: self._delete_selected(permanent=True))
        QShortcut(QKeySequence.SelectAll, self.table_view, self.table_view.selectAll)
        QShortcut(QKeySequence.Copy, self.table_view, self._copy_selected)
        QShortcut(QKeySequence.Cut, self.table_view, self._cut_selected)
        QShortcut(QKeySequence.Paste, self.table_view, self._paste_to_current_folder)
        QShortcut(QKeySequence.Find, self, self.search_bar.setFocus)
        QShortcut(QKeySequence(Qt.ALT | Qt.Key_Left), self, self._go_back)
        QShortcut(QKeySequence(Qt.ALT | Qt.Key_Right), self, self._go_forward)
        QShortcut(QKeySequence(Qt.ALT | Qt.Key_Up), self, self._go_up)
        QShortcut(QKeySequence(Qt.Key_Return), self.table_view, self._activate_selected)
        QShortcut(QKeySequence(Qt.Key_Enter), self.table_view, self._activate_selected)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self.lbl_empty.setGeometry(self.table_view.viewport().geometry())

    def _on_tree_expanded(self, index: QModelIndex):
        item = self.tree_model.itemFromIndex(index)
        if item:
            f_id = item.data(Qt.UserRole)
            self._expanded_folder_ids.add(f_id)

    def _on_tree_collapsed(self, index: QModelIndex):
        item = self.tree_model.itemFromIndex(index)
        if item:
            f_id = item.data(Qt.UserRole)
            self._expanded_folder_ids.discard(f_id)

    def _update_tree(self):
        self.tree_model.clear()
        root = self.tree_model.invisibleRootItem()
        
        folders = self.manager.get_all_folders()
        root_item = QStandardItem(self.style().standardIcon(QStyle.SP_DirIcon), "내 라이브러리")
        root_item.setData(None, Qt.UserRole)
        root.appendRow(root_item)
        
        item_dict = {None: root_item}
        for f in folders:
            item = QStandardItem(self.style().standardIcon(QStyle.SP_DirIcon), f.name)
            item.setData(f.id, Qt.UserRole)
            item_dict[f.id] = item

        for f in folders:
            parent_item = item_dict.get(f.parent_id, root_item)
            parent_item.appendRow(item_dict[f.id])
            
        # Expand persisted expanded folders and ancestors of current folder
        ancestors = set()
        curr = self.current_folder_id
        while curr:
            ancestors.add(curr)
            f_obj = self.manager.db.get_folder(curr)
            curr = f_obj.parent_id if f_obj else None

        for fid, itm in item_dict.items():
            if fid in self._expanded_folder_ids or fid in ancestors or fid is None:
                self.tree_view.setExpanded(itm.index(), True)

    def _update_breadcrumb(self):
        while self.breadcrumb_layout.count():
            child = self.breadcrumb_layout.takeAt(0)
            if child.widget():
                child.widget().deleteLater()

        btn_root = QPushButton("내 라이브러리")
        btn_root.setCursor(Qt.PointingHandCursor)
        if self.current_folder_id is None:
            btn_root.setObjectName("crumb_btn_active")
        else:
            btn_root.setObjectName("crumb_btn")
            btn_root.clicked.connect(lambda: self._navigate(None))
        self.breadcrumb_layout.addWidget(btn_root)

        if not self.current_folder_id:
            self.breadcrumb_layout.addStretch(1)
            return

        path_folders = []
        curr = self.current_folder_id
        while curr:
            f = self.manager.db.get_folder(curr)
            if not f:
                break
            path_folders.append(f)
            curr = f.parent_id
        path_folders.reverse()

        for idx, f in enumerate(path_folders):
            sep = QLabel("›")
            sep.setStyleSheet("color: #484F58; font-weight: bold; padding: 0 2px;")
            self.breadcrumb_layout.addWidget(sep)

            btn = QPushButton(f.name)
            btn.setCursor(Qt.PointingHandCursor)
            is_last = (idx == len(path_folders) - 1)
            if is_last:
                btn.setObjectName("crumb_btn_active")
            else:
                btn.setObjectName("crumb_btn")
                btn.clicked.connect(lambda _, fid=f.id: self._navigate(fid))
            self.breadcrumb_layout.addWidget(btn)

        self.breadcrumb_layout.addStretch(1)

    def _update_table(self, keyword=""):
        self._is_internal_editing = True
        self.table_model.removeRows(0, self.table_model.rowCount())
        
        if keyword:
            scores = self.manager.search(keyword)
            folders = [f for f in self.manager.get_all_folders() if keyword.lower() in f.name.lower()]
        else:
            folders = [f for f in self.manager.get_all_folders() if f.parent_id == self.current_folder_id]
            scores = self.manager.get_folder_scores(self.current_folder_id)
            
        # Apply sorting
        if self._sort_column == 0:
            folders.sort(key=lambda x: x.name.lower(), reverse=(self._sort_order == Qt.DescendingOrder))
            scores.sort(key=lambda x: x.title.lower(), reverse=(self._sort_order == Qt.DescendingOrder))
        elif self._sort_column == 1:
            scores.sort(key=lambda x: (x.file_extension or "").lower(), reverse=(self._sort_order == Qt.DescendingOrder))
        elif self._sort_column == 2:
            scores.sort(key=lambda x: x.duration, reverse=(self._sort_order == Qt.DescendingOrder))
        elif self._sort_column == 3:
            scores.sort(key=lambda x: x.bpm, reverse=(self._sort_order == Qt.DescendingOrder))
        elif self._sort_column == 4:
            scores.sort(key=lambda x: x.total_notes, reverse=(self._sort_order == Qt.DescendingOrder))
        elif self._sort_column == 5:
            folders.sort(key=lambda x: (x.updated_at or x.created_at), reverse=(self._sort_order == Qt.DescendingOrder))
            scores.sort(key=lambda x: (x.updated_at or x.created_at), reverse=(self._sort_order == Qt.DescendingOrder))

        # Add folders first
        for f in folders:
            item_name = QStandardItem(self.style().standardIcon(QStyle.SP_DirIcon), f.name)
            item_name.setData(("folder", f), Qt.UserRole)
            item_name.setEditable(True)
            
            item_type = QStandardItem("파일 폴더")
            item_type.setTextAlignment(Qt.AlignCenter)
            item_type.setEditable(False)
            
            dt = datetime.datetime.fromtimestamp(f.updated_at if f.updated_at else f.created_at)
            item_date = QStandardItem(dt.strftime("%Y-%m-%d %H:%M"))
            item_date.setTextAlignment(Qt.AlignCenter)
            item_date.setEditable(False)
            
            self.table_model.appendRow([
                item_name, item_type,
                QStandardItem(""), QStandardItem(""), QStandardItem(""),
                item_date
            ])
            
        # Add scores
        for s in scores:
            item_name = QStandardItem(self.style().standardIcon(QStyle.SP_FileIcon), s.title)
            item_name.setData(("score", s), Qt.UserRole)
            item_name.setEditable(True)
            item_name.setToolTip(f"{s.title}\n{s.filepath}")
            
            ext_str = s.file_extension.upper()[1:] if s.file_extension else "MIDI"
            item_type = QStandardItem(ext_str)
            item_type.setTextAlignment(Qt.AlignCenter)
            item_type.setEditable(False)
            
            dur_s = int(s.duration)
            mins, secs = divmod(dur_s, 60)
            item_dur = QStandardItem(f"{mins:02d}:{secs:02d}")
            item_dur.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
            item_dur.setEditable(False)
            
            bpm_val = int(s.bpm) if s.bpm > 0 else 120
            item_bpm = QStandardItem(str(bpm_val))
            item_bpm.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
            item_bpm.setEditable(False)
            
            item_notes = QStandardItem(f"{s.total_notes:,}")
            item_notes.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
            item_notes.setEditable(False)
            
            dt = datetime.datetime.fromtimestamp(s.updated_at if s.updated_at else s.created_at)
            item_date = QStandardItem(dt.strftime("%Y-%m-%d %H:%M"))
            item_date.setTextAlignment(Qt.AlignCenter)
            item_date.setEditable(False)
            
            self.table_model.appendRow([item_name, item_type, item_dur, item_bpm, item_notes, item_date])

        self._is_internal_editing = False

        total_items = len(folders) + len(scores)
        if total_items == 0 and not keyword:
            self.lbl_empty.show()
        else:
            self.lbl_empty.hide()

        self._on_selection_changed()

    def _set_sort_column(self, col: int):
        self._sort_column = col
        self._update_table(self.search_bar.text().strip())

    def _set_sort_order(self, order: Qt.SortOrder):
        self._sort_order = order
        self._update_table(self.search_bar.text().strip())

    def _on_selection_changed(self):
        selected = self.table_view.selectionModel().selectedRows()
        count = len(selected)
        
        # Command Bar button enable/disable sync
        self.btn_cut.setEnabled(count > 0)
        self.btn_copy.setEnabled(count > 0)
        self.btn_delete.setEnabled(count > 0)
        self.btn_rename.setEnabled(count == 1)
        self.btn_paste.setEnabled(self.clipboard_data is not None)
        
        # Status Bar update
        total = self.table_model.rowCount()
        status_str = f"{total:,}개 항목"
        if count > 0:
            status_str += f"  |  {count}개 선택됨"
        self.status_bar.setText(status_str)

    def refresh_library(self):
        self._update_tree()
        self._update_table(self.search_bar.text().strip())
        self._update_breadcrumb()
        self._update_nav_buttons()

    def _navigate(self, folder_id: Optional[str], record_history=True):
        if record_history:
            self.history = self.history[:self.history_idx + 1]
            if not self.history or self.history[-1] != folder_id:
                self.history.append(folder_id)
                self.history_idx = len(self.history) - 1
                
        self.current_folder_id = folder_id
        if folder_id:
            self._expanded_folder_ids.add(folder_id)
        self.refresh_library()
        
    def _go_back(self):
        if self.history_idx > 0:
            self.history_idx -= 1
            self._navigate(self.history[self.history_idx], record_history=False)
            
    def _go_forward(self):
        if self.history_idx < len(self.history) - 1:
            self.history_idx += 1
            self._navigate(self.history[self.history_idx], record_history=False)
            
    def _go_up(self):
        if self.current_folder_id:
            f = self.manager.db.get_folder(self.current_folder_id)
            if f:
                self._navigate(f.parent_id)
            else:
                self._navigate(None)
                
    def _update_nav_buttons(self):
        self.btn_back.setEnabled(self.history_idx > 0)
        self.btn_forward.setEnabled(self.history_idx < len(self.history) - 1)
        self.btn_up.setEnabled(self.current_folder_id is not None)

    def _on_tree_clicked(self, index: QModelIndex):
        folder_id = self.tree_model.itemFromIndex(index).data(Qt.UserRole)
        self._navigate(folder_id)

    def _on_table_double_clicked(self, index: QModelIndex):
        item_data = self.table_model.item(index.row(), 0).data(Qt.UserRole)
        if not item_data:
            return
        itype, obj = item_data
        if itype == "folder":
            self._navigate(obj.id)
        elif itype == "score":
            self.score_selected.emit(obj)

    def _activate_selected(self):
        selected = self.table_view.selectionModel().selectedRows()
        if len(selected) == 1:
            self._on_table_double_clicked(selected[0])

    def _on_search(self, text):
        self._update_table(text.strip())

    def _get_selected_data(self) -> list:
        selected_rows = sorted(set(idx.row() for idx in self.table_view.selectionModel().selectedRows()))
        items = []
        for r in selected_rows:
            item = self.table_model.item(r, 0)
            if item:
                data = item.data(Qt.UserRole)
                if data:
                    items.append(data)
        return items

    # --- F2 Inline Rename ---
    def _rename_selected(self):
        selected_rows = self.table_view.selectionModel().selectedRows()
        if len(selected_rows) == 1:
            index = self.table_model.index(selected_rows[0].row(), 0)
            self.table_view.edit(index)

    def _on_item_edited(self, item: QStandardItem):
        if self._is_internal_editing or item.column() != 0:
            return
        
        data = item.data(Qt.UserRole)
        if not data:
            return
            
        itype, obj = data
        new_name = item.text().strip()
        
        try:
            if itype == "folder":
                if new_name and new_name != obj.name:
                    updated_folder = self.manager.rename_folder(obj.id, new_name)
                    item.setData(("folder", updated_folder), Qt.UserRole)
            elif itype == "score":
                if new_name and new_name != obj.title:
                    updated_score = self.manager.rename_score(obj.id, new_name)
                    item.setData(("score", updated_score), Qt.UserRole)
        except Exception as e:
            QMessageBox.critical(self, "이름 변경 오류", f"이름 변경 중 오류가 발생했습니다:\n{e}")
            self.refresh_library()

    # --- Copy / Cut / Paste ---
    def _copy_selected(self):
        items = self._get_selected_data()
        if items:
            self.clipboard_data = {"action": "copy", "items": items}
            self._on_selection_changed()

    def _cut_selected(self):
        items = self._get_selected_data()
        if items:
            self.clipboard_data = {"action": "cut", "items": items}
            self._on_selection_changed()

    def _paste_to_current_folder(self):
        if not self.clipboard_data:
            return
        
        action = self.clipboard_data.get("action")
        items = self.clipboard_data.get("items", [])
        
        for itype, obj in items:
            try:
                if itype == "score":
                    if action == "copy":
                        self.manager.copy_score(obj.id, self.current_folder_id)
                    elif action == "cut":
                        self.manager.move_score(obj.id, self.current_folder_id)
                elif itype == "folder":
                    if action == "cut":
                        self.manager.move_folder(obj.id, self.current_folder_id)
            except Exception as e:
                print(f"Paste failed for {obj}: {e}")

        if action == "cut":
            self.clipboard_data = None
            
        self.refresh_library()

    # --- Delete ---
    def _delete_selected(self, permanent: bool = False):
        items = self._get_selected_data()
        if not items:
            return
            
        count = len(items)
        if permanent:
            msg = f"선택한 {count}개 항목을 완전히 삭제하시겠습니까?\n이 작업은 휴지통을 거치지 않으며 복구할 수 없습니다."
        else:
            msg = f"선택한 {count}개 항목을 휴지통으로 이동하시겠습니까?"
            
        ans = QMessageBox.question(
            self, "삭제 확인", msg,
            QMessageBox.Yes | QMessageBox.No, QMessageBox.No
        )
        if ans == QMessageBox.Yes:
            for itype, obj in items:
                try:
                    if itype == "folder":
                        self.manager.delete_folder(obj.id, permanent=permanent)
                    elif itype == "score":
                        self.manager.delete_score(obj.id, permanent=permanent)
                except Exception as e:
                    QMessageBox.critical(self, "삭제 실패", f"항목 삭제 중 오류가 발생했습니다:\n{e}")
            self.refresh_library()

    # --- Context Menu ---
    def _show_context_menu(self, pos: QPoint):
        items = self._get_selected_data()
        menu = QMenu(self)
        
        if items:
            if len(items) == 1 and items[0][0] == "score":
                act_play = menu.addAction("재생")
                act_play.triggered.connect(lambda: self.score_selected.emit(items[0][1]))
                menu.addSeparator()
                
            act_cut = menu.addAction("잘라내기 (Ctrl+X)")
            act_cut.triggered.connect(self._cut_selected)
            
            act_copy = menu.addAction("복사 (Ctrl+C)")
            act_copy.triggered.connect(self._copy_selected)
            
            if len(items) == 1:
                act_rename = menu.addAction("이름 바꾸기 (F2)")
                act_rename.triggered.connect(self._rename_selected)
                
            act_del = menu.addAction("휴지통으로 삭제 (Del)")
            act_del.triggered.connect(lambda: self._delete_selected(permanent=False))
            
            act_perm_del = menu.addAction("영구 삭제 (Shift+Del)")
            act_perm_del.triggered.connect(lambda: self._delete_selected(permanent=True))
            
            menu.addSeparator()
            
        if self.clipboard_data:
            act_paste = menu.addAction("여기에 붙여넣기 (Ctrl+V)")
            act_paste.triggered.connect(self._paste_to_current_folder)
            menu.addSeparator()
            
        act_new_folder = menu.addAction("새 폴더 만들기")
        act_new_folder.triggered.connect(self._create_folder)
        
        act_add_file = menu.addAction("파일 추가...")
        act_add_file.triggered.connect(self._import_files_dialog)
        
        menu.exec(self.table_view.viewport().mapToGlobal(pos))

    # --- Dialog Actions ---
    def _create_folder(self):
        name, ok = QInputDialog.getText(self, "새 폴더", "폴더 이름:")
        if ok and name.strip():
            self.manager.create_folder(name.strip(), self.current_folder_id)
            self.refresh_library()

    def _import_files_dialog(self):
        files, _ = QFileDialog.getOpenFileNames(
            self, "악보 파일 가져오기", "",
            "지원 파일 (*.mid *.midi *.musicxml *.xml *.mxl *.pdf *.png *.jpg *.jpeg *.txt *.mml);;모든 파일 (*.*)"
        )
        if files:
            for f in files:
                try:
                    self.manager.import_external_file(f, folder_id=self.current_folder_id)
                except Exception as e:
                    print(f"Failed to import {f}: {e}")
            self.refresh_library()

    def _import_folder_dialog(self):
        dir_path = QFileDialog.getExistingDirectory(self, "가져올 Windows 폴더 선택")
        if dir_path:
            self._start_folder_import(dir_path, target_parent_folder_id=self.current_folder_id)

    def _open_mml_dialog(self):
        dlg = MmlDialog(self.manager, self.current_folder_id, self)
        if dlg.exec():
            self.refresh_library()
            if dlg.saved_score_item:
                self.score_selected.emit(dlg.saved_score_item)

    def _start_folder_import(self, source_folder_path: str, target_parent_folder_id: Optional[str]):
        progress_dlg = QProgressDialog("폴더 구조 및 악보를 가져오는 중...", "취소", 0, 100, self)
        progress_dlg.setWindowModality(Qt.WindowModal)
        progress_dlg.setAutoClose(True)
        progress_dlg.setAutoReset(True)
        progress_dlg.setMinimumDuration(0)
        progress_dlg.show()

        worker = FolderImportWorker(self.manager, source_folder_path, target_parent_folder_id, self)

        def on_progress(cur, total, name):
            progress_dlg.setMaximum(max(1, total))
            progress_dlg.setValue(cur)
            progress_dlg.setLabelText(f"가져오는 중 ({cur}/{total}):\n{name}")

        def on_finished(summary):
            progress_dlg.close()
            self.refresh_library()
            msg = f"폴더 가져오기 완료:\n- 생성된 폴더: {summary['folders_created']}개\n- 가져온 악보: {summary['scores_imported']}개"
            if summary.get("skipped_existing", 0) > 0:
                msg += f"\n- 중복 건너뜀: {summary['skipped_existing']}개"
            QMessageBox.information(self, "가져오기 완료", msg)

        def on_error(err_msg):
            progress_dlg.close()
            self.refresh_library()
            QMessageBox.critical(self, "가져오기 오류", f"폴더 가져오기 중 오류가 발생했습니다:\n{err_msg}")

        progress_dlg.canceled.connect(worker.cancel)
        worker.sig_progress.connect(on_progress)
        worker.sig_finished.connect(on_finished)
        worker.sig_error.connect(on_error)
        worker.start()

    # --- Internal & External Drag and Drop Engine ---
    def _start_internal_drag(self):
        selected_data = self._get_selected_data()
        if not selected_data:
            return

        scores = [obj.id for itype, obj in selected_data if itype == "score"]
        folders = [obj.id for itype, obj in selected_data if itype == "folder"]

        if not scores and not folders:
            return

        mime_data = QMimeData()
        payload = json.dumps({"scores": scores, "folders": folders})
        mime_data.setData("application/x-roblox-piano-items", QByteArray(payload.encode("utf-8")))

        drag = QDrag(self.table_view)
        drag.setMimeData(mime_data)
        drag.exec(Qt.MoveAction)

    def _get_drop_target_folder_id(self, source, pos: QPoint) -> Optional[str]:
        if source is self.tree_view.viewport():
            index = self.tree_view.indexAt(pos)
            if index.isValid():
                item = self.tree_model.itemFromIndex(index)
                if item:
                    return item.data(Qt.UserRole)
            return None
        elif source is self.table_view.viewport():
            index = self.table_view.indexAt(pos)
            if index.isValid():
                item = self.table_model.item(index.row(), 0)
                if item:
                    data = item.data(Qt.UserRole)
                    if data and data[0] == "folder":
                        return data[1].id
            return self.current_folder_id
        return self.current_folder_id

    def eventFilter(self, source, event):
        if not hasattr(self, 'table_view') or not hasattr(self, 'tree_view'):
            return super().eventFilter(source, event)

        if source in (self.table_view.viewport(), self.tree_view.viewport()):
            if source is self.table_view.viewport():
                if event.type() == QEvent.Type.MouseButtonPress:
                    if event.button() == Qt.LeftButton:
                        self._drag_start_pos = event.pos()
                elif event.type() == QEvent.Type.MouseMove:
                    if (event.buttons() & Qt.LeftButton) and self._drag_start_pos:
                        if (event.pos() - self._drag_start_pos).manhattanLength() >= QApplication.startDragDistance():
                            self._start_internal_drag()
                            self._drag_start_pos = None
                            return True

            if event.type() in (QEvent.Type.DragEnter, QEvent.Type.DragMove):
                if event.mimeData().hasUrls() or event.mimeData().hasFormat("application/x-roblox-piano-items"):
                    event.acceptProposedAction()
                    return True

            elif event.type() == QEvent.Type.Drop:
                pos = event.position().toPoint()
                target_folder_id = self._get_drop_target_folder_id(source, pos)

                # 1. Internal Drag & Drop (Move scores / folders)
                if event.mimeData().hasFormat("application/x-roblox-piano-items"):
                    try:
                        raw = event.mimeData().data("application/x-roblox-piano-items").data().decode("utf-8")
                        payload = json.loads(raw)
                        scores = payload.get("scores", [])
                        folders = payload.get("folders", [])

                        for f_id in folders:
                            if f_id == target_folder_id or self.manager.is_descendant(f_id, target_folder_id):
                                QMessageBox.warning(self, "이동 불가", "자기 자신이나 하위 폴더로는 이동할 수 없습니다.")
                                event.acceptProposedAction()
                                return True

                        for s_id in scores:
                            self.manager.move_score(s_id, target_folder_id)

                        for f_id in folders:
                            self.manager.move_folder(f_id, target_folder_id)

                        self.refresh_library()
                    except Exception as e:
                        QMessageBox.critical(self, "이동 오류", f"항목 이동 중 오류가 발생했습니다:\n{e}")
                    event.acceptProposedAction()
                    return True

                # 2. External Drag & Drop from Windows Explorer (Copy)
                elif event.mimeData().hasUrls():
                    urls = event.mimeData().urls()
                    has_folder = False
                    for url in urls:
                        if url.isLocalFile():
                            loc = url.toLocalFile()
                            if os.path.isdir(loc):
                                has_folder = True
                                self._start_folder_import(loc, target_parent_folder_id=target_folder_id)
                            else:
                                try:
                                    self.manager.import_external_file(loc, folder_id=target_folder_id)
                                except Exception as e:
                                    print(f"Failed to import dropped file {loc}: {e}")
                    if not has_folder:
                        self.refresh_library()
                    event.acceptProposedAction()
                    return True

        return super().eventFilter(source, event)


class FolderImportWorker(QThread):
    sig_progress = Signal(int, int, str)
    sig_finished = Signal(dict)
    sig_error = Signal(str)

    def __init__(self, manager: LibraryManager, source_folder_path: str, target_parent_folder_id: Optional[str] = None, parent=None):
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
                cancel_check=lambda: self._is_cancelled
            )
            self.sig_finished.emit(summary)
        except Exception as e:
            self.sig_error.emit(str(e))

    def _on_progress(self, cur: int, total: int, name: str):
        self.sig_progress.emit(cur, total, name)
