from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QLineEdit, QTableWidget, QTableWidgetItem, QHeaderView, QMessageBox
)

from src.library.manager import LibraryManager
from src.library.models import ScoreItem


class LibraryWidget(QWidget):
    """
    Displays the list of scores stored in the local library.
    """
    score_selected = Signal(ScoreItem)

    def __init__(self, library_manager: LibraryManager, parent=None):
        super().__init__(parent)
        self.manager = library_manager
        self._setup_ui()
        self.refresh_library()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(10)

        # Header
        header_layout = QHBoxLayout()
        lbl_title = QLabel("내 라이브러리")
        lbl_title.setStyleSheet("font-size: 18px; font-weight: bold;")
        
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("악보 검색 (제목, 태그)...")
        self.search_input.setFixedWidth(200)
        self.search_input.textChanged.connect(self._on_search)

        btn_refresh = QPushButton("새로고침")
        btn_refresh.clicked.connect(self.refresh_library)

        header_layout.addWidget(lbl_title)
        header_layout.addStretch()
        header_layout.addWidget(self.search_input)
        header_layout.addWidget(btn_refresh)
        
        layout.addLayout(header_layout)

        # Table
        self.table = QTableWidget(0, 5)
        self.table.setHorizontalHeaderLabels(["제목", "소스", "길이", "노트 수", "태그"])
        self.table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self.table.setSelectionBehavior(QTableWidget.SelectRows)
        self.table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.table.itemDoubleClicked.connect(self._on_item_double_clicked)
        
        layout.addWidget(self.table)
        
        # Bottom Actions
        actions_layout = QHBoxLayout()
        self.btn_load = QPushButton("선택한 곡 불러오기")
        self.btn_load.setObjectName("primary_btn")
        self.btn_load.clicked.connect(self._load_selected)
        
        self.btn_delete = QPushButton("삭제")
        self.btn_delete.clicked.connect(self._delete_selected)
        
        actions_layout.addStretch()
        actions_layout.addWidget(self.btn_delete)
        actions_layout.addWidget(self.btn_load)
        
        layout.addLayout(actions_layout)

    def refresh_library(self):
        self._populate_table(self.manager.get_all())

    def _on_search(self, text: str):
        if not text.strip():
            self.refresh_library()
        else:
            self._populate_table(self.manager.search(text))

    def _populate_table(self, items: list[ScoreItem]):
        self.table.setRowCount(0)
        self._current_items = items
        for row, item in enumerate(items):
            self.table.insertRow(row)
            
            # Title
            title_widget = QTableWidgetItem(item.title)
            title_widget.setData(Qt.UserRole, item)  # Store object
            self.table.setItem(row, 0, title_widget)
            
            # Source
            self.table.setItem(row, 1, QTableWidgetItem(item.source_type))
            
            # Duration
            mins, secs = divmod(int(item.duration), 60)
            self.table.setItem(row, 2, QTableWidgetItem(f"{mins:02d}:{secs:02d}"))
            
            # Notes
            self.table.setItem(row, 3, QTableWidgetItem(str(item.total_notes)))
            
            # Tags
            self.table.setItem(row, 4, QTableWidgetItem(item.tags))

    def _get_selected_item(self) -> ScoreItem | None:
        selected_rows = self.table.selectionModel().selectedRows()
        if not selected_rows:
            return None
        row = selected_rows[0].row()
        return self.table.item(row, 0).data(Qt.UserRole)

    def _load_selected(self):
        item = self._get_selected_item()
        if item:
            self.score_selected.emit(item)

    def _on_item_double_clicked(self, item: QTableWidgetItem):
        self._load_selected()

    def _delete_selected(self):
        item = self._get_selected_item()
        if not item:
            return
            
        ans = QMessageBox.question(
            self, "삭제 확인", f"'{item.title}' 악보를 라이브러리에서 삭제하시겠습니까?\n(실제 파일도 삭제됩니다)",
            QMessageBox.Yes | QMessageBox.No
        )
        if ans == QMessageBox.Yes:
            self.manager.delete_score(item.id)
            self.refresh_library()
