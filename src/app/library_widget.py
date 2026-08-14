import os
import datetime
from PySide6.QtCore import Qt, Signal, QPoint
from PySide6.QtGui import QAction, QIcon, QCursor
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QLineEdit, QTableWidget, QTableWidgetItem, QHeaderView, QMessageBox,
    QComboBox, QMenu, QInputDialog, QStackedWidget, QFrame
)

from src.library.manager import LibraryManager
from src.library.models import ScoreItem


class LibraryWidget(QWidget):
    """
    Displays the list of scores stored in the local library.
    """
    score_selected = Signal(ScoreItem)
    add_external_requested = Signal()

    def __init__(self, library_manager: LibraryManager, parent=None):
        super().__init__(parent)
        self.manager = library_manager
        # Let drops bubble up to main_window
        self.setAcceptDrops(False) 
        self._setup_ui()
        self.refresh_library()

    def _setup_ui(self):
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(16, 16, 16, 16)
        self.main_layout.setSpacing(16)

        # Header Area
        header_layout = QHBoxLayout()
        header_layout.setSpacing(12)
        
        lbl_title = QLabel("내 라이브러리")
        lbl_title.setStyleSheet("font-size: 20px; font-weight: bold; color: #FFFFFF;")
        
        self.btn_add_external = QPushButton("외부 파일 추가")
        self.btn_add_external.setObjectName("primary_btn")
        self.btn_add_external.setToolTip("지원 파일: MIDI, MusicXML, 이미지, PDF 등")
        self.btn_add_external.clicked.connect(lambda: self.add_external_requested.emit())

        header_layout.addWidget(lbl_title)
        header_layout.addStretch()
        header_layout.addWidget(self.btn_add_external)
        self.main_layout.addLayout(header_layout)

        # Filter & Search Bar
        filter_layout = QHBoxLayout()
        filter_layout.setSpacing(10)
        
        self.search_input = QLineEdit()
        self.search_input.setPlaceholderText("악보 검색 (제목, 포맷)...")
        self.search_input.setMinimumWidth(250)
        self.search_input.textChanged.connect(self._on_search_changed)
        
        self.sort_combo = QComboBox()
        self.sort_combo.addItems(["최신 추가순", "오래된순", "이름순", "길이순"])
        self.sort_combo.currentIndexChanged.connect(self._on_sort_changed)

        self.filter_combo = QComboBox()
        self.filter_combo.addItems(["모든 포맷", "MIDI / XML", "PDF / 이미지", "YouTube"])
        self.filter_combo.currentIndexChanged.connect(self._on_filter_changed)

        btn_refresh = QPushButton("새로고침")
        btn_refresh.clicked.connect(self.refresh_library)

        filter_layout.addWidget(self.search_input)
        filter_layout.addWidget(self.filter_combo)
        filter_layout.addWidget(self.sort_combo)
        filter_layout.addStretch()
        filter_layout.addWidget(btn_refresh)
        
        self.main_layout.addLayout(filter_layout)

        # Stacked Widget for Empty State vs Table
        self.stack = QStackedWidget()
        self.main_layout.addWidget(self.stack, 1)

        # 1. Empty State
        self._build_empty_state()

        # 2. Table State
        self._build_table()

    def _build_empty_state(self):
        self.empty_widget = QFrame()
        self.empty_widget.setObjectName("card")
        layout = QVBoxLayout(self.empty_widget)
        layout.setAlignment(Qt.AlignCenter)
        layout.setSpacing(15)

        # Use an SVG icon instead of emoji
        lbl_icon = QLabel()
        # Use an appropriate unicode box or SVG placeholder if available
        lbl_icon.setText("⛁") # monochrome symbol
        lbl_icon.setStyleSheet("font-size: 50px; color: #64748B;")
        lbl_icon.setAlignment(Qt.AlignCenter)

        lbl_text = QLabel("라이브러리가 비어 있습니다.\\n\\n악보 파일(MIDI, XML, PDF, 이미지)을 이 창에 드래그 앤 드롭하거나\\n우측 상단의 '외부 파일 추가' 버튼을 눌러보세요.")
        lbl_text.setStyleSheet("font-size: 14px; color: #94A3B8; line-height: 1.5;")
        lbl_text.setAlignment(Qt.AlignCenter)

        layout.addWidget(lbl_icon)
        layout.addWidget(lbl_text)

        self.stack.addWidget(self.empty_widget)

    def _build_table(self):
        self.table = QTableWidget(0, 6)
        self.table.setHorizontalHeaderLabels(["제목 (원본 파일명)", "포맷", "길이", "노트 수", "상태", "추가일"])
        self.table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(2, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(3, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(4, QHeaderView.ResizeToContents)
        self.table.horizontalHeader().setSectionResizeMode(5, QHeaderView.ResizeToContents)
        
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

        self.stack.addWidget(self.table)

    def refresh_library(self):
        self._apply_filters()

    def _on_search_changed(self, text: str):
        self._apply_filters()

    def _on_sort_changed(self, index: int):
        self._apply_filters()

    def _on_filter_changed(self, index: int):
        self._apply_filters()

    def _apply_filters(self):
        # 1. Fetch raw items
        search_text = self.search_input.text().strip().lower()
        if search_text:
            items = self.manager.search(search_text)
        else:
            items = self.manager.get_all()

        # 2. Format filter
        filter_idx = self.filter_combo.currentIndex()
        if filter_idx == 1: # MIDI / XML
            items = [it for it in items if it.file_extension in [".mid", ".midi", ".xml", ".mxl", ".musicxml"]]
        elif filter_idx == 2: # PDF / Image
            items = [it for it in items if it.file_extension in [".pdf", ".png", ".jpg", ".jpeg"]]
        elif filter_idx == 3: # YouTube
            items = [it for it in items if it.source_type == "YOUTUBE"]

        # 3. Sort
        sort_idx = self.sort_combo.currentIndex()
        if sort_idx == 0: # Newest
            items.sort(key=lambda x: x.created_at, reverse=True)
        elif sort_idx == 1: # Oldest
            items.sort(key=lambda x: x.created_at)
        elif sort_idx == 2: # Name
            items.sort(key=lambda x: x.title.lower())
        elif sort_idx == 3: # Duration
            items.sort(key=lambda x: x.duration, reverse=True)

        self._populate_table(items)

    def _populate_table(self, items: list[ScoreItem]):
        if not items and not self.search_input.text().strip():
            # Truly empty (no search, no items)
            self.stack.setCurrentIndex(0)
            return
        
        self.stack.setCurrentIndex(1)
        self.table.setRowCount(0)
        self._current_items = items
        
        for row, item in enumerate(items):
            self.table.insertRow(row)
            
            # Title
            title_text = item.title
            if item.original_filename and item.original_filename != item.title:
                title_text += f" ({item.original_filename})"
            title_widget = QTableWidgetItem(title_text)
            title_widget.setData(Qt.UserRole, item)
            self.table.setItem(row, 0, title_widget)
            
            # Format
            fmt_str = item.file_extension.upper().replace(".", "") if item.file_extension else item.source_type
            self.table.setItem(row, 1, QTableWidgetItem(fmt_str))
            
            # Duration
            mins, secs = divmod(int(item.duration), 60)
            self.table.setItem(row, 2, QTableWidgetItem(f"{mins:02d}:{secs:02d}"))
            
            # Notes
            self.table.setItem(row, 3, QTableWidgetItem(f"{item.total_notes:,}"))
            
            # Status
            status_map = {
                "READY": "[+] 준비됨",
                "ANALYZING": "[-] 분석 중",
                "ANALYSIS_REQUIRED": "[!] 분석 필요",
                "ANALYSIS_FAILED": "[x] 분석 실패",
                "UNSUPPORTED": "[-] 미지원"
            }
            status_text = status_map.get(item.analysis_status, item.analysis_status)
            self.table.setItem(row, 4, QTableWidgetItem(status_text))

            # Date
            dt = datetime.datetime.fromtimestamp(item.created_at)
            self.table.setItem(row, 5, QTableWidgetItem(dt.strftime("%Y-%m-%d %H:%M")))

    def _get_selected_items(self) -> list[ScoreItem]:
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
        if item:
            self._load_selected(item)

    def _show_context_menu(self, pos: QPoint):
        items = self._get_selected_items()
        if not items:
            return

        menu = QMenu(self)
        menu.setStyleSheet("""
            QMenu { background-color: #1E293B; border: 1px solid #334155; }
            QMenu::item { padding: 6px 24px; color: #F8FAFC; }
            QMenu::item:selected { background-color: #3B82F6; }
        """)

        if len(items) == 1:
            item = items[0]
            action_load = menu.addAction("> 재생 (불러오기)")
            action_load.triggered.connect(lambda: self._load_selected(item))
            
            menu.addSeparator()
            
            action_rename = menu.addAction("* 제목 변경")
            action_rename.triggered.connect(lambda: self._rename_item(item))
            
            action_open_dir = menu.addAction("^ 파일 위치 열기")
            action_open_dir.triggered.connect(lambda: self._open_file_location(item))
            
            menu.addSeparator()

        action_delete = menu.addAction(f"x 삭제 ({len(items)}개)")
        action_delete.triggered.connect(lambda: self._delete_items(items))

        menu.exec(self.table.viewport().mapToGlobal(pos))

    def _rename_item(self, item: ScoreItem):
        new_title, ok = QInputDialog.getText(
            self, "제목 변경", "새로운 제목을 입력하세요:", QLineEdit.Normal, item.title
        )
        if ok and new_title.strip():
            item.title = new_title.strip()
            item.updated_at = datetime.datetime.now().timestamp()
            self.manager.db.update_score(item)
            self.refresh_library()

    def _open_file_location(self, item: ScoreItem):
        import subprocess
        if os.path.exists(item.filepath):
            # Select file in Windows Explorer
            subprocess.run(['explorer', '/select,', os.path.normpath(item.filepath)])
        else:
            QMessageBox.warning(self, "오류", "해당 파일이 실제 경로에 존재하지 않습니다.")

    def _delete_items(self, items: list[ScoreItem]):
        if not items:
            return
            
        msg = f"선택한 악보 {len(items)}개를 삭제하시겠습니까?\\n(로컬 파일도 함께 삭제됩니다)"
        ans = QMessageBox.question(self, "삭제 확인", msg, QMessageBox.Yes | QMessageBox.No)
        
        if ans == QMessageBox.Yes:
            for item in items:
                self.manager.delete_score(item.id)
            self.refresh_library()
