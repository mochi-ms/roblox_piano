import os
import datetime
import uuid
import shutil
from PySide6.QtCore import Qt, Signal, QPoint, QUrl, QMimeData, QDir
from PySide6.QtGui import QAction, QIcon, QCursor, QDrag, QMouseEvent
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QLineEdit, QTableWidget, QTableWidgetItem, QHeaderView, QMessageBox,
    QComboBox, QMenu, QInputDialog, QStackedWidget, QFrame,
    QSplitter, QTreeView, QAbstractItemView
)
from PySide6.QtGui import QStandardItemModel, QStandardItem

from src.library.manager import LibraryManager
from src.library.models import ScoreItem, FolderItem
from src.app.mml_dialog import MmlDialog
from src.importers.mml_importer import MmlImporter

class DraggableTableWidget(QTableWidget):
    dropped_urls = Signal(list)
    
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.setAcceptDrops(True)
        self.setDragEnabled(True)
        self.setDragDropMode(QAbstractItemView.DragDrop)
        self.setDefaultDropAction(Qt.CopyAction)
        self._drag_start_pos = None

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            super().dragEnterEvent(event)

    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            super().dragMoveEvent(event)

    def dropEvent(self, event):
        if event.mimeData().hasUrls():
            urls = event.mimeData().urls()
            self.dropped_urls.emit(urls)
            event.acceptProposedAction()
        else:
            super().dropEvent(event)

    def mousePressEvent(self, event: QMouseEvent):
        if event.button() == Qt.LeftButton:
            self._drag_start_pos = event.pos()
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event: QMouseEvent):
        if not (event.buttons() & Qt.LeftButton) or not self._drag_start_pos:
            return
            
        if (event.pos() - self._drag_start_pos).manhattanLength() < 5:
            return
            
        selected_items = self.selectedItems()
        if not selected_items:
            return
            
        urls = []
        for row in set(item.row() for item in selected_items):
            item_data = self.item(row, 0).data(Qt.UserRole)
            if isinstance(item_data, ScoreItem):
                if os.path.exists(item_data.filepath):
                    urls.append(QUrl.fromLocalFile(os.path.normpath(item_data.filepath)))
        
        if urls:
            drag = QDrag(self)
            mime_data = QMimeData()
            mime_data.setUrls(urls)
            drag.setMimeData(mime_data)
            drag.exec(Qt.CopyAction)


class LibraryWidget(QWidget):
    score_selected = Signal(ScoreItem)
    add_external_requested = Signal()

    def __init__(self, library_manager: LibraryManager, parent=None):
        super().__init__(parent)
        self.manager = library_manager
        self.setAcceptDrops(True)
        self.current_folder_id = None
        
        self._setup_ui()
        self.refresh_library()

    def _setup_ui(self):
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(16, 16, 16, 16)
        self.main_layout.setSpacing(12)

        # Toolbar Area
        toolbar_layout = QHBoxLayout()
        toolbar_layout.setSpacing(8)
        
        self.btn_back = QPushButton("◀ 뒤로")
        self.btn_back.clicked.connect(self._go_back)
        self.btn_up = QPushButton("▲ 상위 폴더")
        self.btn_up.clicked.connect(self._go_up)
        
        self.lbl_breadcrumb = QLabel("내 라이브러리")
        self.lbl_breadcrumb.setStyleSheet("font-size: 16px; font-weight: bold; color: #FFFFFF;")
        
        toolbar_layout.addWidget(self.btn_back)
        toolbar_layout.addWidget(self.btn_up)
        toolbar_layout.addWidget(self.lbl_breadcrumb)
        toolbar_layout.addStretch()
        
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("검색...")
        self.search_input.setMinimumWidth(200)
        self.search_input.textChanged.connect(self._on_search_changed)
        
        self.sort_combo = QComboBox()
        self.sort_combo.addItems(["이름순", "최신 추가순", "길이순"])
        self.sort_combo.currentIndexChanged.connect(self._on_sort_changed)

        btn_new_folder = QPushButton("새 폴더")
        btn_new_folder.clicked.connect(self._create_folder)
        
        btn_add_file = QPushButton("파일 추가")
        btn_add_file.clicked.connect(self.add_external_requested.emit)
        
        btn_mml = QPushButton("MML 붙여넣기")
        btn_mml.clicked.connect(self._open_mml_dialog)
        btn_mml.setObjectName("primary_btn")

        toolbar_layout.addWidget(self.search_input)
        toolbar_layout.addWidget(self.sort_combo)
        toolbar_layout.addWidget(btn_new_folder)
        toolbar_layout.addWidget(btn_add_file)
        toolbar_layout.addWidget(btn_mml)
        
        self.main_layout.addLayout(toolbar_layout)

        # Splitter for Tree and Table
        self.splitter = QSplitter(Qt.Horizontal)
        self.main_layout.addWidget(self.splitter, 1)

        # Left: TreeView
        self.tree_view = QTreeView()
        self.tree_view.setHeaderHidden(True)
        self.tree_model = QStandardItemModel()
        self.tree_view.setModel(self.tree_model)
        self.tree_view.clicked.connect(self._on_tree_clicked)
        self.tree_view.setStyleSheet("""
            QTreeView {
                background-color: #0F172A;
                color: #E2E8F0;
                border: 1px solid #1E293B;
                border-radius: 8px;
            }
            QTreeView::item:selected {
                background-color: #3B82F6;
            }
        """)
        self.splitter.addWidget(self.tree_view)

        # Right: TableWidget
        self.table = DraggableTableWidget(0, 5)
        self.table.setHorizontalHeaderLabels(["이름", "형식", "재생 시간", "노트 수", "수정한 날짜"])
        self.table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(2, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(3, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(4, QHeaderView.ResizeToContents)
        
        self.table.setSelectionBehavior(QTableWidget.SelectRows)
        self.table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.table.setStyleSheet("""
            QTableWidget {
                border: 1px solid #1E293B;
                border-radius: 8px;
                background-color: #0F172A;
                gridline-color: #1E293B;
                color: #E2E8F0;
                selection-background-color: #3B82F6;
            }
            QHeaderView::section {
                background-color: #1E293B;
                color: #94A3B8;
                border: none;
                font-weight: bold;
                padding: 4px;
            }
        """)
        
        self.table.itemDoubleClicked.connect(self._on_item_double_clicked)
        self.table.setContextMenuPolicy(Qt.CustomContextMenu)
        self.table.customContextMenuRequested.connect(self._show_context_menu)
        self.table.dropped_urls.connect(self._on_files_dropped)

        self.splitter.addWidget(self.table)
        self.splitter.setSizes([200, 600])

        self._folder_history = []
        self._build_tree()

    def _build_tree(self):
        self.tree_model.clear()
        root_item = QStandardItem("내 라이브러리")
        root_item.setData(None, Qt.UserRole)
        self.tree_model.appendRow(root_item)
        
        folders = self.manager.get_all_folders()
        folder_dict = {f.id: f for f in folders}
        item_dict = {None: root_item}
        
        # Build hierarchy
        for f in folders:
            item = QStandardItem(f"📁 {f.name}")
            item.setData(f.id, Qt.UserRole)
            item_dict[f.id] = item
            
        for f in folders:
            parent_item = item_dict.get(f.parent_id, root_item)
            parent_item.appendRow(item_dict[f.id])
            
        self.tree_view.expandAll()

    def refresh_library(self):
        self._build_tree()
        self._apply_filters()
        self._update_breadcrumb()

    def _update_breadcrumb(self):
        if not self.current_folder_id:
            self.lbl_breadcrumb.setText("내 라이브러리")
        else:
            folder = self.manager.db.get_folder(self.current_folder_id)
            path = []
            while folder:
                path.insert(0, folder.name)
                folder = self.manager.db.get_folder(folder.parent_id) if folder.parent_id else None
            self.lbl_breadcrumb.setText("내 라이브러리 > " + " > ".join(path))
            
        self.btn_back.setEnabled(len(self._folder_history) > 0)
        self.btn_up.setEnabled(self.current_folder_id is not None)

    def _on_search_changed(self, text: str):
        self._apply_filters()

    def _on_sort_changed(self, index: int):
        self._apply_filters()

    def _go_back(self):
        if self._folder_history:
            self.current_folder_id = self._folder_history.pop()
            self._apply_filters()
            self._update_breadcrumb()

    def _go_up(self):
        if self.current_folder_id:
            self._folder_history.append(self.current_folder_id)
            folder = self.manager.db.get_folder(self.current_folder_id)
            self.current_folder_id = folder.parent_id if folder else None
            self._apply_filters()
            self._update_breadcrumb()

    def _on_tree_clicked(self, index):
        item = self.tree_model.itemFromIndex(index)
        folder_id = item.data(Qt.UserRole)
        if self.current_folder_id != folder_id:
            if self.current_folder_id is not None:
                self._folder_history.append(self.current_folder_id)
            self.current_folder_id = folder_id
            self._apply_filters()
            self._update_breadcrumb()

    def _apply_filters(self):
        search_text = self.search_input.text().strip().lower()
        
        if search_text:
            items = self.manager.search(search_text)
            # Find folders matching search
            all_folders = self.manager.get_all_folders()
            folders = [f for f in all_folders if search_text in f.name.lower()]
        else:
            all_items = self.manager.get_all()
            items = [it for it in all_items if it.folder_id == self.current_folder_id]
            all_folders = self.manager.get_all_folders()
            folders = [f for f in all_folders if f.parent_id == self.current_folder_id]

        sort_idx = self.sort_combo.currentIndex()
        if sort_idx == 0: # Name
            items.sort(key=lambda x: x.title.lower())
            folders.sort(key=lambda x: x.name.lower())
        elif sort_idx == 1: # Newest
            items.sort(key=lambda x: x.created_at, reverse=True)
            folders.sort(key=lambda x: x.created_at, reverse=True)
        elif sort_idx == 2: # Duration
            items.sort(key=lambda x: x.duration, reverse=True)

        self._populate_table(folders, items)

    def _populate_table(self, folders: list[FolderItem], scores: list[ScoreItem]):
        self.table.setRowCount(0)
        
        row = 0
        for f in folders:
            self.table.insertRow(row)
            
            title_widget = QTableWidgetItem(f"📁 {f.name}")
            title_widget.setData(Qt.UserRole, f)
            self.table.setItem(row, 0, title_widget)
            self.table.setItem(row, 1, QTableWidgetItem("폴더"))
            self.table.setItem(row, 2, QTableWidgetItem("-"))
            self.table.setItem(row, 3, QTableWidgetItem("-"))
            
            dt = datetime.datetime.fromtimestamp(f.updated_at if f.updated_at else f.created_at)
            self.table.setItem(row, 4, QTableWidgetItem(dt.strftime("%Y-%m-%d %H:%M")))
            row += 1
            
        for item in scores:
            self.table.insertRow(row)
            
            title_text = item.title
            if item.original_filename and item.original_filename != item.title:
                title_text += f" ({item.original_filename})"
            title_widget = QTableWidgetItem(f"🎵 {title_text}")
            title_widget.setData(Qt.UserRole, item)
            self.table.setItem(row, 0, title_widget)
            
            fmt_str = item.file_extension.upper().replace(".", "") if item.file_extension else item.source_type
            self.table.setItem(row, 1, QTableWidgetItem(fmt_str))
            
            mins, secs = divmod(int(item.duration), 60)
            self.table.setItem(row, 2, QTableWidgetItem(f"{mins:02d}:{secs:02d}"))
            self.table.setItem(row, 3, QTableWidgetItem(f"{item.total_notes:,}"))
            
            dt = datetime.datetime.fromtimestamp(item.created_at)
            self.table.setItem(row, 4, QTableWidgetItem(dt.strftime("%Y-%m-%d %H:%M")))
            row += 1

    def _get_selected_data(self) -> list:
        selected_rows = set(idx.row() for idx in self.table.selectionModel().selectedRows())
        items = []
        for r in selected_rows:
            item = self.table.item(r, 0).data(Qt.UserRole)
            if item:
                items.append(item)
        return items

    def _load_selected(self, item: ScoreItem):
        if item.analysis_status == "READY":
            self.score_selected.emit(item)
        else:
            QMessageBox.warning(self, "재생 불가", "해당 악보는 아직 분석이 완료되지 않았거나 분석에 실패했습니다.")

    def _on_item_double_clicked(self, table_item: QTableWidgetItem):
        item = self.table.item(table_item.row(), 0).data(Qt.UserRole)
        if isinstance(item, FolderItem):
            self._folder_history.append(self.current_folder_id)
            self.current_folder_id = item.id
            self._apply_filters()
            self._update_breadcrumb()
        elif isinstance(item, ScoreItem):
            self._load_selected(item)

    def _show_context_menu(self, pos: QPoint):
        items = self._get_selected_data()
        if not items:
            return

        menu = QMenu(self)
        menu.setStyleSheet("""
            QMenu { background-color: #1E293B; border: 1px solid #334155; color: #F8FAFC; }
            QMenu::item { padding: 6px 24px; }
            QMenu::item:selected { background-color: #3B82F6; }
        """)

        if len(items) == 1:
            item = items[0]
            if isinstance(item, ScoreItem):
                action_load = menu.addAction("재생")
                action_load.triggered.connect(lambda: self._load_selected(item))
                menu.addSeparator()
                action_open_dir = menu.addAction("파일 위치 열기")
                action_open_dir.triggered.connect(lambda: self._open_file_location(item))
                
            action_rename = menu.addAction("이름 변경 (F2)")
            action_rename.triggered.connect(lambda: self._rename_item(item))
            
            menu.addSeparator()

        action_delete = menu.addAction(f"삭제 ({len(items)}개)")
        action_delete.triggered.connect(lambda: self._delete_items(items))

        menu.exec(self.table.viewport().mapToGlobal(pos))

    def _create_folder(self):
        name, ok = QInputDialog.getText(self, "새 폴더", "폴더 이름을 입력하세요:")
        if ok and name.strip():
            self.manager.create_folder(name.strip(), self.current_folder_id)
            self.refresh_library()

    def _rename_item(self, item):
        if isinstance(item, FolderItem):
            name, ok = QInputDialog.getText(self, "이름 변경", "새 이름을 입력하세요:", QLineEdit.Normal, item.name)
            if ok and name.strip():
                item.name = name.strip()
                item.updated_at = datetime.datetime.now().timestamp()
                self.manager.update_folder(item)
                self.refresh_library()
        elif isinstance(item, ScoreItem):
            new_title, ok = QInputDialog.getText(self, "제목 변경", "새로운 제목을 입력하세요:", QLineEdit.Normal, item.title)
            if ok and new_title.strip():
                item.title = new_title.strip()
                item.updated_at = datetime.datetime.now().timestamp()
                self.manager.db.update_score(item)
                self.refresh_library()

    def _open_file_location(self, item: ScoreItem):
        import subprocess
        if os.path.exists(item.filepath):
            subprocess.run(['explorer', '/select,', os.path.normpath(item.filepath)])
        else:
            QMessageBox.warning(self, "오류", "해당 파일이 실제 경로에 존재하지 않습니다.")

    def _delete_items(self, items: list):
        if not items:
            return
            
        msg = f"선택한 항목 {len(items)}개를 삭제하시겠습니까?\\n(악보의 경우 로컬 파일도 함께 삭제됩니다)"
        ans = QMessageBox.question(self, "삭제 확인", msg, QMessageBox.Yes | QMessageBox.No)
        
        if ans == QMessageBox.Yes:
            for item in items:
                if isinstance(item, FolderItem):
                    self.manager.delete_folder(item.id)
                elif isinstance(item, ScoreItem):
                    self.manager.delete_score(item.id)
            self.refresh_library()

    def _open_mml_dialog(self):
        dialog = MmlDialog(self)
        if dialog.exec() == QDialog.Accepted:
            mml_code = dialog.get_mml_code()
            if mml_code:
                try:
                    title, ok = QInputDialog.getText(self, "MML 제목", "악보 제목을 입력하세요:", QLineEdit.Normal, "새로운 MML 악보")
                    if not ok or not title.strip():
                        title = f"MML 악보 {datetime.datetime.now().strftime('%Y-%m-%d %H%M')}"
                    
                    importer = MmlImporter()
                    temp_midi = os.path.join(self.manager.library_dir, "temp_mml_import.mid")
                    importer.convert_to_midi(mml_code, temp_midi)
                    
                    self.manager.import_external_file(temp_midi, source_type="MML", folder_id=self.current_folder_id)
                    os.remove(temp_midi)
                    
                    self.refresh_library()
                    QMessageBox.information(self, "완료", "MML 악보가 성공적으로 추가되었습니다.")
                except Exception as e:
                    QMessageBox.critical(self, "오류", f"MML 변환 중 오류가 발생했습니다:\\n{e}")

    def _on_files_dropped(self, urls: list):
        for url in urls:
            if url.isLocalFile():
                filepath = url.toLocalFile()
                try:
                    ext = os.path.splitext(filepath)[1].lower()
                    if ext == '.mml':
                        with open(filepath, 'r', encoding='utf-8') as f:
                            mml_code = f.read()
                        
                        importer = MmlImporter()
                        temp_midi = os.path.join(self.manager.library_dir, "temp_mml_import.mid")
                        importer.convert_to_midi(mml_code, temp_midi)
                        
                        item = self.manager.import_external_file(temp_midi, source_type="MML", folder_id=self.current_folder_id)
                        # Fix title to match original MML filename
                        item.title = os.path.splitext(os.path.basename(filepath))[0]
                        item.original_filename = os.path.basename(filepath)
                        self.manager.db.update_score(item)
                        
                        os.remove(temp_midi)
                    else:
                        self.manager.import_external_file(filepath, folder_id=self.current_folder_id)
                except Exception as e:
                    print(f"Failed to import dropped file {filepath}: {e}")
                    
        self.refresh_library()

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            super().dragEnterEvent(event)

    def dropEvent(self, event):
        if event.mimeData().hasUrls():
            self._on_files_dropped(event.mimeData().urls())
            event.acceptProposedAction()
        else:
            super().dropEvent(event)
