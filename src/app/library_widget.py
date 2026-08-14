import os
import datetime
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QLineEdit,
    QTreeView, QTableView, QHeaderView, QMenu, QInputDialog, QMessageBox,
    QLabel, QSplitter, QAbstractItemView, QFrame, QToolButton
)
from PySide6.QtCore import Qt, QPoint, Signal, QModelIndex
from PySide6.QtGui import QStandardItemModel, QStandardItem, QIcon, QAction, QDragEnterEvent, QDropEvent
from typing import Optional

from src.library.manager import LibraryManager
from src.library.models import ScoreItem, FolderItem
from src.app.mml_dialog import MmlDialog
from src.importers.mml_importer import MmlImporter

class LibraryWidget(QWidget):
    score_selected = Signal(ScoreItem)

    def __init__(self, manager: LibraryManager, parent=None):
        super().__init__(parent)
        self.manager = manager
        
        # History for back/forward
        self.history = []
        self.history_idx = -1
        
        self.current_folder_id = None
        self._setup_ui()
        self._navigate(None)

    def _setup_ui(self):
        # Apply standard Windows 11 style, no emojis
        self.setStyleSheet("""
            QWidget {
                background-color: #f3f3f3;
                color: #1a1a1a;
                font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif;
                font-size: 14px;
            }
            QSplitter::handle {
                background-color: #e5e5e5;
                width: 1px;
            }
            QTreeView, QTableView {
                background-color: #ffffff;
                border: 1px solid #e5e5e5;
                border-radius: 4px;
                outline: none;
            }
            QTreeView::item:selected, QTableView::item:selected {
                background-color: #cce8ff;
                color: #000000;
            }
            QHeaderView::section {
                background-color: #f9f9f9;
                padding: 4px;
                border: none;
                border-bottom: 1px solid #e5e5e5;
                border-right: 1px solid #e5e5e5;
            }
            QPushButton, QToolButton {
                background-color: #ffffff;
                border: 1px solid #cccccc;
                border-radius: 4px;
                padding: 6px 12px;
            }
            QPushButton:hover, QToolButton:hover {
                background-color: #f0f0f0;
            }
            QLineEdit {
                background-color: #ffffff;
                border: 1px solid #cccccc;
                border-radius: 4px;
                padding: 6px;
            }
            #breadcrumb {
                background-color: #ffffff;
                border: 1px solid #e5e5e5;
                border-radius: 4px;
                padding: 6px;
            }
            #breadcrumbLabel {
                color: #0078d4;
            }
            #breadcrumbLabel:hover {
                text-decoration: underline;
                cursor: pointer;
            }
        """)
        
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(10, 10, 10, 10)
        main_layout.setSpacing(10)
        
        # Toolbar
        toolbar = QHBoxLayout()
        toolbar.setSpacing(8)
        
        self.btn_back = QToolButton()
        self.btn_back.setText("<")
        self.btn_back.clicked.connect(self._go_back)
        
        self.btn_forward = QToolButton()
        self.btn_forward.setText(">")
        self.btn_forward.clicked.connect(self._go_forward)
        
        self.btn_up = QToolButton()
        self.btn_up.setText("^")
        self.btn_up.clicked.connect(self._go_up)
        
        toolbar.addWidget(self.btn_back)
        toolbar.addWidget(self.btn_forward)
        toolbar.addWidget(self.btn_up)
        
        self.breadcrumb = QLabel()
        self.breadcrumb.setObjectName("breadcrumb")
        toolbar.addWidget(self.breadcrumb, 1)
        
        self.search_bar = QLineEdit()
        self.search_bar.setPlaceholderText("검색...")
        self.search_bar.textChanged.connect(self._on_search)
        self.search_bar.setFixedWidth(250)
        toolbar.addWidget(self.search_bar)
        
        btn_add_folder = QPushButton("새 폴더")
        btn_add_folder.clicked.connect(self._create_folder)
        toolbar.addWidget(btn_add_folder)
        
        btn_mml = QPushButton("MML 추가")
        btn_mml.clicked.connect(self._open_mml_dialog)
        toolbar.addWidget(btn_mml)
        
        main_layout.addLayout(toolbar)
        
        # Splitter
        self.splitter = QSplitter(Qt.Horizontal)
        main_layout.addWidget(self.splitter, 1)
        
        # Left: TreeView
        self.tree_view = QTreeView()
        self.tree_view.setHeaderHidden(True)
        self.tree_view.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.tree_view.clicked.connect(self._on_tree_clicked)
        self.splitter.addWidget(self.tree_view)
        
        # Right: TableView
        self.table_view = QTableView()
        self.table_view.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.table_view.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.table_view.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.table_view.verticalHeader().setVisible(False)
        self.table_view.setShowGrid(False)
        self.table_view.setAlternatingRowColors(True)
        self.table_view.doubleClicked.connect(self._on_table_double_clicked)
        self.table_view.setContextMenuPolicy(Qt.CustomContextMenu)
        self.table_view.customContextMenuRequested.connect(self._show_context_menu)
        
        # Enable Drag and Drop
        self.table_view.setAcceptDrops(True)
        self.table_view.setDragEnabled(True)
        self.table_view.setDropIndicatorShown(True)
        self.table_view.setDragDropMode(QAbstractItemView.DragDrop)
        self.table_view.viewport().installEventFilter(self)
        
        self.splitter.addWidget(self.table_view)
        self.splitter.setSizes([200, 600])

        self.tree_model = QStandardItemModel()
        self.tree_view.setModel(self.tree_model)
        
        self.table_model = QStandardItemModel()
        self.table_model.setHorizontalHeaderLabels(["이름", "유형", "길이", "노트 수", "수정 날짜"])
        self.table_view.setModel(self.table_model)
        self.table_view.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)

    def _update_tree(self):
        self.tree_model.clear()
        root = self.tree_model.invisibleRootItem()
        
        folders = self.manager.get_all_folders()
        folder_dict = {f.id: f for f in folders}
        item_dict = {}
        
        # Add a special "Library Root"
        root_item = QStandardItem("Library")
        root_item.setData(None, Qt.UserRole)
        root.appendRow(root_item)
        item_dict[None] = root_item

        for f in folders:
            item = QStandardItem(f.name)
            item.setData(f.id, Qt.UserRole)
            item_dict[f.id] = item

        for f in folders:
            parent_item = item_dict.get(f.parent_id, root_item)
            parent_item.appendRow(item_dict[f.id])
            
        self.tree_view.expandAll()

    def _update_breadcrumb(self):
        if not self.current_folder_id:
            self.breadcrumb.setText("Library")
            return
            
        path = []
        curr = self.current_folder_id
        while curr:
            f = self.manager.db.get_folder(curr)
            if not f: break
            path.append(f.name)
            curr = f.parent_id
            
        path.reverse()
        self.breadcrumb.setText("Library > " + " > ".join(path))

    def _update_table(self, keyword=""):
        self.table_model.removeRows(0, self.table_model.rowCount())
        
        if keyword:
            # Search mode
            scores = self.manager.search(keyword)
            folders = [f for f in self.manager.get_all_folders() if keyword.lower() in f.name.lower()]
        else:
            # Normal browse mode
            folders = [f for f in self.manager.get_all_folders() if f.parent_id == self.current_folder_id]
            scores = self.manager.get_folder_scores(self.current_folder_id)
            
        # Add folders
        for f in folders:
            item_name = QStandardItem(f.name)
            item_name.setData(("folder", f), Qt.UserRole)
            item_type = QStandardItem("파일 폴더")
            
            dt = datetime.datetime.fromtimestamp(f.updated_at if f.updated_at else f.created_at)
            item_date = QStandardItem(dt.strftime("%Y-%m-%d %H:%M"))
            
            self.table_model.appendRow([item_name, item_type, QStandardItem(""), QStandardItem(""), item_date])
            
        # Add scores
        for s in scores:
            item_name = QStandardItem(s.title)
            item_name.setData(("score", s), Qt.UserRole)
            item_type = QStandardItem(s.file_extension.upper()[1:] if s.file_extension else "File")
            
            mins, secs = divmod(int(s.duration), 60)
            item_dur = QStandardItem(f"{mins:02d}:{secs:02d}")
            item_notes = QStandardItem(f"{s.total_notes:,}")
            
            dt = datetime.datetime.fromtimestamp(s.created_at)
            item_date = QStandardItem(dt.strftime("%Y-%m-%d %H:%M"))
            
            self.table_model.appendRow([item_name, item_type, item_dur, item_notes, item_date])

    def refresh_library(self):
        self._update_tree()
        self._update_table()
        self._update_breadcrumb()
        self._update_nav_buttons()

    def _navigate(self, folder_id: Optional[str], record_history=True):
        if record_history:
            # Truncate future history
            self.history = self.history[:self.history_idx+1]
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
                
    def _update_nav_buttons(self):
        self.btn_back.setEnabled(self.history_idx > 0)
        self.btn_forward.setEnabled(self.history_idx < len(self.history) - 1)
        self.btn_up.setEnabled(self.current_folder_id is not None)

    def _on_tree_clicked(self, index: QModelIndex):
        folder_id = self.tree_model.itemFromIndex(index).data(Qt.UserRole)
        self._navigate(folder_id)

    def _on_table_double_clicked(self, index: QModelIndex):
        item_data = self.table_model.item(index.row(), 0).data(Qt.UserRole)
        if not item_data: return
        
        itype, obj = item_data
        if itype == "folder":
            self._navigate(obj.id)
        elif itype == "score":
            if obj.analysis_status == "READY":
                self.score_selected.emit(obj)
            else:
                QMessageBox.warning(self, "재생 불가", "해당 악보는 아직 분석이 완료되지 않았거나 실패했습니다.")

    def _on_search(self, text):
        self._update_table(text)

    def _get_selected_data(self) -> list:
        selected_rows = set(idx.row() for idx in self.table_view.selectionModel().selectedRows())
        items = []
        for r in selected_rows:
            data = self.table_model.item(r, 0).data(Qt.UserRole)
            if data:
                items.append(data)
        return items

    def _show_context_menu(self, pos: QPoint):
        items = self._get_selected_data()
        if not items:
            # Context menu for background (empty space)
            menu = QMenu(self)
            action_new_folder = menu.addAction("새 폴더")
            action_new_folder.triggered.connect(self._create_folder)
            menu.exec(self.table_view.viewport().mapToGlobal(pos))
            return

        menu = QMenu(self)

        if len(items) == 1:
            itype, obj = items[0]
            if itype == "score":
                action_load = menu.addAction("재생")
                action_load.triggered.connect(lambda: self.score_selected.emit(obj))
                menu.addSeparator()
                action_open_dir = menu.addAction("파일 위치 열기")
                action_open_dir.triggered.connect(lambda: self._open_file_location(obj))
                
            action_rename = menu.addAction("이름 변경(F2)")
            action_rename.triggered.connect(lambda: self._rename_item(itype, obj))
            
            menu.addSeparator()

        action_delete = menu.addAction(f"삭제 ({len(items)}개)")
        action_delete.triggered.connect(lambda: self._delete_items(items))

        menu.exec(self.table_view.viewport().mapToGlobal(pos))

    def _create_folder(self):
        name, ok = QInputDialog.getText(self, "새 폴더", "폴더 이름을 입력하세요:")
        if ok and name.strip():
            self.manager.create_folder(name.strip(), self.current_folder_id)
            self.refresh_library()

    def _rename_item(self, itype, obj):
        if itype == "folder":
            name, ok = QInputDialog.getText(self, "이름 변경", "새 이름을 입력하세요:", QLineEdit.Normal, obj.name)
            if ok and name.strip():
                obj.name = name.strip()
                obj.updated_at = datetime.datetime.now().timestamp()
                self.manager.update_folder(obj)
                self.refresh_library()
        elif itype == "score":
            new_title, ok = QInputDialog.getText(self, "제목 변경", "새로운 제목을 입력하세요:", QLineEdit.Normal, obj.title)
            if ok and new_title.strip():
                obj.title = new_title.strip()
                obj.updated_at = datetime.datetime.now().timestamp()
                self.manager.db.update_score(obj)
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
            
        msg = f"선택한 {len(items)}개를 삭제하시겠습니까?\n(물리적 파일과 폴더가 휴지통으로 이동합니다)"
        ans = QMessageBox.question(self, "삭제 확인", msg, QMessageBox.Yes | QMessageBox.No)
        
        if ans == QMessageBox.Yes:
            for itype, obj in items:
                if itype == "folder":
                    self.manager.delete_folder(obj.id)
                elif itype == "score":
                    self.manager.delete_score(obj.id)
            self.refresh_library()

    def _open_mml_dialog(self):
        dialog = MmlDialog(self)
        if dialog.exec() == QDialog.Accepted:
            mml_code = dialog.get_mml_code()
            if mml_code:
                try:
                    title = dialog.get_title()
                    
                    importer = MmlImporter()
                    import tempfile
                    fd, temp_midi = tempfile.mkstemp(suffix=".mid")
                    os.close(fd)
                    
                    importer.convert_to_midi(mml_code, temp_midi)
                    
                    # Instead of direct conversion, create a dummy file with title name in temp?
                    # No, import_external_file copies it. We can just rename it during import by overriding original_filename.
                    # Or we just rename the tempfile first.
                    renamed_temp = os.path.join(os.path.dirname(temp_midi), title + ".mid")
                    if os.path.exists(renamed_temp): os.remove(renamed_temp) # Just in case
                    os.rename(temp_midi, renamed_temp)
                    
                    item = self.manager.import_external_file(renamed_temp, source_type="MML", folder_id=self.current_folder_id)
                    os.remove(renamed_temp)
                    
                    self.refresh_library()
                    QMessageBox.information(self, "완료", "MML 악보가 성공적으로 추가되었습니다.")
                    
                    if dialog.should_play():
                        self.score_selected.emit(item)
                        
                except Exception as e:
                    QMessageBox.critical(self, "오류", f"MML 변환 중 오류가 발생했습니다:\n{e}")

    # --- Drag and Drop for TableView Viewport ---
    def eventFilter(self, source, event):
        if source is self.table_view.viewport():
            if event.type() == event.Type.DragEnter:
                if event.mimeData().hasUrls():
                    event.acceptProposedAction()
                    return True
            elif event.type() == event.Type.Drop:
                if event.mimeData().hasUrls():
                    self._on_files_dropped(event.mimeData().urls())
                    event.acceptProposedAction()
                    return True
        return super().eventFilter(source, event)

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
                        import tempfile
                        fd, temp_midi = tempfile.mkstemp(suffix=".mid")
                        os.close(fd)
                        importer.convert_to_midi(mml_code, temp_midi)
                        
                        title = os.path.splitext(os.path.basename(filepath))[0]
                        renamed_temp = os.path.join(os.path.dirname(temp_midi), title + ".mid")
                        if os.path.exists(renamed_temp): os.remove(renamed_temp)
                        os.rename(temp_midi, renamed_temp)
                        
                        item = self.manager.import_external_file(renamed_temp, source_type="MML", folder_id=self.current_folder_id)
                        os.remove(renamed_temp)
                    else:
                        self.manager.import_external_file(filepath, folder_id=self.current_folder_id)
                except Exception as e:
                    print(f"Failed to import dropped file {filepath}: {e}")
                    
        self.refresh_library()
