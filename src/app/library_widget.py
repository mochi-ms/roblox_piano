import os
import json
import datetime
import subprocess
from typing import Optional, List, Tuple

from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLineEdit,
    QTreeView, QTableView, QHeaderView, QMenu, QInputDialog, QMessageBox,
    QLabel, QSplitter, QAbstractItemView, QFrame, QToolButton, QFileDialog,
    QStyledItemDelegate, QStyle, QProgressDialog, QApplication
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
        
        # History for back/forward navigation
        self.history: List[Optional[str]] = []
        self.history_idx = -1
        self.current_folder_id: Optional[str] = None
        
        # Internal clipboard for copy / cut
        # Format: {"action": "copy" | "cut", "items": [("folder"|"score", obj)]}
        self.clipboard_data = None
        
        self._is_internal_editing = False
        self._drag_start_pos = None

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
                padding: 5px 12px;
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
                padding: 4px 6px;
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
                padding: 4px 6px;
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
        main_layout.setContentsMargins(12, 12, 12, 8)
        main_layout.setSpacing(8)
        
        # 1. Top Navigation & Action Toolbar
        toolbar = QHBoxLayout()
        toolbar.setSpacing(6)
        
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
        
        toolbar.addWidget(self.btn_back)
        toolbar.addWidget(self.btn_forward)
        toolbar.addWidget(self.btn_up)
        
        # Clickable Breadcrumb Container
        self.breadcrumb_container = QWidget(self)
        self.breadcrumb_layout = QHBoxLayout(self.breadcrumb_container)
        self.breadcrumb_layout.setContentsMargins(4, 0, 4, 0)
        self.breadcrumb_layout.setSpacing(2)
        toolbar.addWidget(self.breadcrumb_container, 1)
        
        # Search Box
        self.search_bar = QLineEdit(self)
        self.search_bar.setPlaceholderText("라이브러리 검색 (Ctrl+F)...")
        self.search_bar.textChanged.connect(self._on_search)
        self.search_bar.setMinimumWidth(160)
        self.search_bar.setMaximumWidth(280)
        toolbar.addWidget(self.search_bar)
        
        # Action Buttons
        btn_add_folder = QPushButton("새 폴더", self)
        btn_add_folder.clicked.connect(self._create_folder)
        toolbar.addWidget(btn_add_folder)

        btn_add_file = QPushButton("파일 추가", self)
        btn_add_file.clicked.connect(self._import_files_dialog)
        toolbar.addWidget(btn_add_file)
        
        btn_import_folder = QPushButton("폴더 추가", self)
        btn_import_folder.setToolTip("Windows 폴더 계층을 그대로 라이브러리로 가져옵니다")
        btn_import_folder.clicked.connect(self._import_folder_dialog)
        toolbar.addWidget(btn_import_folder)
        
        btn_mml = QPushButton("MML 가져오기", self)
        btn_mml.setStyleSheet("background-color: #2D4C7C; color: #FFFFFF; border: 1px solid #4C82F7; font-weight: 600;")
        btn_mml.clicked.connect(self._open_mml_dialog)
        toolbar.addWidget(btn_mml)
        
        main_layout.addLayout(toolbar)
        
        # 2. Main Splitter (Left Tree + Right Table)
        self.splitter = QSplitter(Qt.Horizontal, self)
        main_layout.addWidget(self.splitter, 1)
        
        # Left Tree View
        self.tree_view = QTreeView(self)
        self.tree_view.setHeaderHidden(True)
        self.tree_view.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.tree_view.setIndentation(18)
        self.tree_view.setUniformRowHeights(True)
        self.tree_view.clicked.connect(self._on_tree_clicked)
        
        # Enable Drops on TreeView
        self.tree_view.setAcceptDrops(True)
        self.tree_view.viewport().installEventFilter(self)
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
        
        # Enable Drag and Drop on TableView
        self.table_view.setAcceptDrops(True)
        self.table_view.setDragEnabled(True)
        self.table_view.setDropIndicatorShown(True)
        self.table_view.viewport().setAcceptDrops(True)
        self.table_view.viewport().installEventFilter(self)
        
        table_layout.addWidget(self.table_view, 1)

        # Empty state label overlay
        self.lbl_empty = QLabel("이 폴더에 악보가 없습니다.\n파일을 끌어오거나 '파일 추가'를 선택하세요.", self.table_view)
        self.lbl_empty.setAlignment(Qt.AlignCenter)
        self.lbl_empty.setStyleSheet("color: #758195; font-size: 13px; line-height: 1.5;")
        self.lbl_empty.hide()

        self.splitter.addWidget(table_container)
        self.splitter.setSizes([220, 680])

        self.tree_model = QStandardItemModel(self)
        self.tree_view.setModel(self.tree_model)
        
        self.table_model = QStandardItemModel(self)
        self.table_model.setHorizontalHeaderLabels(["이름", "형식", "재생 시간", "BPM", "노트 수", "수정한 날짜"])
        self.table_model.itemChanged.connect(self._on_item_edited)
        self.table_view.setModel(self.table_model)
        
        # Configure column resize modes
        header = self.table_view.horizontalHeader()
        header.setFixedHeight(34)
        header.setSectionResizeMode(0, QHeaderView.Stretch)
        header.setSectionResizeMode(1, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(2, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(3, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(4, QHeaderView.ResizeToContents)
        header.setSectionResizeMode(5, QHeaderView.ResizeToContents)

        self.table_view.selectionModel().selectionChanged.connect(self._update_status_bar)

        # Bottom Status Bar
        self.status_bar = QLabel(self)
        self.status_bar.setObjectName("statusBar")
        main_layout.addWidget(self.status_bar)

    def _setup_shortcuts(self):
        # Keyboard shortcuts (Windows Explorer standard)
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
            
        self.tree_view.expandAll()

    def _update_breadcrumb(self):
        # Clear existing breadcrumb items
        while self.breadcrumb_layout.count():
            child = self.breadcrumb_layout.takeAt(0)
            if child.widget():
                child.widget().deleteLater()

        # Root button
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

        # Build folder hierarchy list
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

        self._update_status_bar()

    def _update_status_bar(self):
        total = self.table_model.rowCount()
        selected = len(self.table_view.selectionModel().selectedRows())
        
        status_str = f"{total:,}개 항목"
        if selected > 0:
            status_str += f"  |  {selected}개 선택됨"
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

    def _cut_selected(self):
        items = self._get_selected_data()
        if items:
            self.clipboard_data = {"action": "cut", "items": items}

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
                if itype == "folder":
                    self.manager.delete_folder(obj.id, permanent=permanent)
                elif itype == "score":
                    self.manager.delete_score(obj.id, permanent=permanent)
            self.refresh_library()

    # --- Context Menu ---
    def _show_context_menu(self, pos: QPoint):
        items = self._get_selected_data()
        menu = QMenu(self)
        
        if not items:
            action_new_folder = menu.addAction("새 폴더")
            action_new_folder.triggered.connect(self._create_folder)
            
            action_paste = menu.addAction("붙여넣기 (Ctrl+V)")
            action_paste.triggered.connect(self._paste_to_current_folder)
            action_paste.setEnabled(bool(self.clipboard_data))
            
            menu.addSeparator()
            action_add_file = menu.addAction("파일 추가...")
            action_add_file.triggered.connect(self._import_files_dialog)
            
            action_add_folder = menu.addAction("폴더 추가...")
            action_add_folder.triggered.connect(self._import_folder_dialog)
            
            action_mml = menu.addAction("MML 가져오기...")
            action_mml.triggered.connect(self._open_mml_dialog)
        else:
            if len(items) == 1:
                itype, obj = items[0]
                if itype == "score":
                    action_load = menu.addAction("재생")
                    action_load.triggered.connect(lambda: self.score_selected.emit(obj))
                    menu.addSeparator()
                elif itype == "folder":
                    action_open = menu.addAction("열기")
                    action_open.triggered.connect(lambda: self._navigate(obj.id))
                    menu.addSeparator()
                    
            action_cut = menu.addAction("잘라내기 (Ctrl+X)")
            action_cut.triggered.connect(self._cut_selected)
            
            action_copy = menu.addAction("복사 (Ctrl+C)")
            action_copy.triggered.connect(self._copy_selected)
            
            menu.addSeparator()
            
            if len(items) == 1:
                action_rename = menu.addAction("이름 바꾸기 (F2)")
                action_rename.triggered.connect(self._rename_selected)
                
            action_delete = menu.addAction(f"삭제 ({len(items)}개)")
            action_delete.triggered.connect(lambda: self._delete_selected(permanent=False))
            
            if len(items) == 1 and items[0][0] == "score":
                menu.addSeparator()
                action_location = menu.addAction("파일 위치 열기")
                action_location.triggered.connect(lambda: self._open_file_location(items[0][1]))
                
        menu.exec(self.table_view.viewport().mapToGlobal(pos))

    def _open_file_location(self, item: ScoreItem):
        if os.path.exists(item.filepath):
            subprocess.run(['explorer', '/select,', os.path.normpath(item.filepath)])
        else:
            QMessageBox.warning(self, "오류", "해당 파일이 실제 디렉터리에 존재하지 않습니다.")

    def _create_folder(self):
        name, ok = QInputDialog.getText(self, "새 폴더", "폴더 이름을 입력하세요:")
        if ok and name.strip():
            self.manager.create_folder(name.strip(), self.current_folder_id)
            self.refresh_library()

    def _import_files_dialog(self):
        files, _ = QFileDialog.getOpenFileNames(
            self, "악보 파일 가져오기", "",
            "악보 파일 (*.mid *.midi *.xml *.mxl *.musicxml *.pdf *.mml);;모든 파일 (*.*)"
        )
        for f in files:
            try:
                self.manager.import_external_file(f, folder_id=self.current_folder_id)
            except Exception as e:
                print(f"Import failed for {f}: {e}")
        self.refresh_library()

    def _import_folder_dialog(self):
        folder = QFileDialog.getExistingDirectory(self, "가져올 악보 최상위 폴더 선택")
        if folder and os.path.exists(folder):
            self._start_folder_import(folder)

    def _start_folder_import(self, folder_path: str, target_parent_folder_id: Optional[str] = None):
        if target_parent_folder_id is None:
            target_parent_folder_id = self.current_folder_id

        progress = QProgressDialog("악보 폴더 분석 및 가져오는 중...", "취소", 0, 100, self)
        progress.setWindowTitle("폴더 가져오기")
        progress.setWindowModality(Qt.WindowModal)
        progress.setMinimumDuration(0)
        progress.setValue(0)
        progress.setStyleSheet("""
            QProgressDialog {
                background-color: #161B22;
                color: #C9D1D9;
            }
            QLabel {
                color: #F0F6FC;
                font-size: 13px;
            }
            QProgressBar {
                background-color: #0D1117;
                border: 1px solid #30363D;
                border-radius: 4px;
                text-align: center;
                color: #FFFFFF;
            }
            QProgressBar::chunk {
                background-color: #4C82F7;
                border-radius: 3px;
            }
            QPushButton {
                background-color: #21262D;
                color: #C9D1D9;
                border: 1px solid #30363D;
                border-radius: 4px;
                padding: 4px 12px;
            }
            QPushButton:hover {
                background-color: #30363D;
                color: #FFFFFF;
            }
        """)

        worker = FolderImportWorker(self.manager, folder_path, target_parent_folder_id, self)

        def on_progress(cur, total, name):
            if total > 0:
                progress.setMaximum(total)
                progress.setValue(cur)
                progress.setLabelText(f"가져오는 중... ({cur} / {total})\n현재: {name}")

        def on_finished(summary):
            progress.close()
            self.refresh_library()
            if summary.get("cancelled"):
                QMessageBox.information(self, "가져오기 중단", "사용자에 의해 가져오기가 중단되었습니다.\n지금까지 완료된 항목은 안전하게 저장되었습니다.")
            else:
                msg = (
                    f"악보 폴더 가져오기가 완료되었습니다.\n\n"
                    f"· 생성/병합된 폴더: {summary['imported_folders']}개\n"
                    f"· 가져온 악보: {summary['imported_scores']}개\n"
                    f"· 건너뜀 (비악보 파일): {summary['skipped']}개\n"
                    f"· 실패: {summary['failed']}개"
                )
                QMessageBox.information(self, "가져오기 완료", msg)

        def on_error(err):
            progress.close()
            self.refresh_library()
            QMessageBox.critical(self, "가져오기 오류", f"폴더 가져오기 중 오류가 발생했습니다:\n{err}")

        worker.sig_progress.connect(on_progress)
        worker.sig_finished.connect(on_finished)
        worker.sig_error.connect(on_error)
        progress.canceled.connect(worker.cancel)

        progress.show()
        worker.start()

    def _open_mml_dialog(self):
        dialog = MmlDialog(self.manager, current_folder_id=self.current_folder_id, parent=self)
        if dialog.exec() == MmlDialog.Accepted:
            self.refresh_library()
            item = dialog.get_created_score_item()
            if item and dialog.should_play():
                self.score_selected.emit(item)

    # --- Drag & Drop Engine ---
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
            # Detect mouse drag initiation on TableView
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

                        # Cycle prevention for folder move
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
