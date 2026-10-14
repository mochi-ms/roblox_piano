import datetime
from typing import List, Optional, Tuple

from PySide6.QtCore import QAbstractTableModel, QModelIndex, Qt, Signal
from PySide6.QtGui import QBrush, QColor, QFont
from PySide6.QtWidgets import QApplication, QStyle

from src.library.manager import LibraryManager
from src.library.models import FolderItem, ScoreItem


LibraryEntry = Tuple[str, object]


class LibraryTableModel(QAbstractTableModel):
    """
    Lightweight, paged QAbstractTableModel.

    QTableView only asks data for visible cells, so unlike QStandardItemModel
    we do not allocate 6 QStandardItem objects for every library row.
    """

    error = Signal(str)
    folder_structure_changed = Signal()

    HEADERS = ["이름", "형식", "재생 시간", "BPM", "노트 수", "수정한 날짜"]

    SORT_FIELD_BY_COLUMN = {
        0: "title",
        1: "file_extension",
        2: "duration",
        3: "bpm",
        4: "total_notes",
        5: "updated_at",
    }

    def __init__(self, manager: LibraryManager, parent=None, page_size: int = 250):
        super().__init__(parent)
        self.manager = manager
        self.page_size = max(50, int(page_size))

        self.folder_id: Optional[str] = None
        self.scope = "folder"  # folder | favorites | recent
        self.keyword = ""
        self.sort_column = 0
        self.descending = False

        self._folder_rows: List[LibraryEntry] = []
        self._score_rows: List[LibraryEntry] = []
        self._total_score_count = 0
        self._total_folder_count = 0
        style = QApplication.style()
        self._folder_icon = style.standardIcon(QStyle.SP_DirIcon)
        self._file_icon = style.standardIcon(QStyle.SP_FileIcon)

    # --------------------------------------------------------------
    # Qt model
    # --------------------------------------------------------------
    def rowCount(self, parent=QModelIndex()):
        if parent.isValid():
            return 0
        return len(self._folder_rows) + len(self._score_rows)

    def columnCount(self, parent=QModelIndex()):
        return len(self.HEADERS)

    def headerData(self, section, orientation, role=Qt.DisplayRole):
        if orientation == Qt.Horizontal and role == Qt.DisplayRole:
            if 0 <= section < len(self.HEADERS):
                return self.HEADERS[section]
        return super().headerData(section, orientation, role)

    def _entry(self, row: int) -> Optional[LibraryEntry]:
        if row < 0:
            return None
        if row < len(self._folder_rows):
            return self._folder_rows[row]
        score_index = row - len(self._folder_rows)
        if 0 <= score_index < len(self._score_rows):
            return self._score_rows[score_index]
        return None

    def item_at(self, row: int) -> Optional[LibraryEntry]:
        return self._entry(row)

    def data(self, index, role=Qt.DisplayRole):
        if not index.isValid():
            return None

        entry = self._entry(index.row())
        if entry is None:
            return None

        kind, obj = entry
        col = index.column()

        if role == Qt.UserRole:
            return entry

        if role == Qt.ToolTipRole:
            if kind == "score":
                return f"{obj.title}\n{obj.filepath}"
            return obj.name

        if role == Qt.DecorationRole and col == 0:
            return self._folder_icon if kind == "folder" else self._file_icon

        if role == Qt.FontRole and col == 0 and kind == "folder":
            font = QFont()
            font.setWeight(QFont.Medium)
            return font

        if role == Qt.ForegroundRole and kind == "score" and getattr(obj, "favorite", False) and col == 0:
            return QBrush(QColor("#E6EDF3"))

        if role == Qt.TextAlignmentRole:
            if col in (2, 3, 4):
                return int(Qt.AlignRight | Qt.AlignVCenter)
            if col in (1, 5):
                return int(Qt.AlignCenter)
            return int(Qt.AlignLeft | Qt.AlignVCenter)

        if role not in (Qt.DisplayRole, Qt.EditRole):
            return None

        if kind == "folder":
            if col == 0:
                return obj.name
            if col == 1:
                return "폴더"
            if col in (2, 3, 4):
                return ""
            if col == 5:
                ts = obj.updated_at or obj.created_at
                return datetime.datetime.fromtimestamp(ts).strftime("%Y-%m-%d %H:%M")
            return ""

        # Score
        if col == 0:
            return obj.title
        if col == 1:
            if obj.file_extension:
                return obj.file_extension.lstrip(".").upper()
            return (obj.source_type or "MIDI").upper()
        if col == 2:
            seconds = max(0, int(obj.duration or 0))
            minutes, secs = divmod(seconds, 60)
            return f"{minutes:02d}:{secs:02d}"
        if col == 3:
            return str(int(obj.bpm or 120))
        if col == 4:
            return f"{int(obj.total_notes or 0):,}"
        if col == 5:
            ts = obj.updated_at or obj.created_at
            return datetime.datetime.fromtimestamp(ts).strftime("%Y-%m-%d %H:%M")
        return ""

    def flags(self, index):
        if not index.isValid():
            return Qt.ItemIsEnabled | Qt.ItemIsDropEnabled

        flags = Qt.ItemIsEnabled | Qt.ItemIsSelectable | Qt.ItemIsDragEnabled
        if index.column() == 0:
            flags |= Qt.ItemIsEditable
        return flags

    def setData(self, index, value, role=Qt.EditRole):
        if role != Qt.EditRole or not index.isValid() or index.column() != 0:
            return False

        entry = self._entry(index.row())
        if not entry:
            return False

        kind, obj = entry
        new_name = str(value).strip()
        if not new_name:
            return False

        try:
            if kind == "folder":
                if new_name == obj.name:
                    return True
                updated = self.manager.rename_folder(obj.id, new_name)
                if index.row() < len(self._folder_rows):
                    self._folder_rows[index.row()] = ("folder", updated)
                self.folder_structure_changed.emit()
            else:
                if new_name == obj.title:
                    return True
                updated = self.manager.rename_score(obj.id, new_name)
                score_index = index.row() - len(self._folder_rows)
                self._score_rows[score_index] = ("score", updated)

            self.dataChanged.emit(index, index, [Qt.DisplayRole, Qt.EditRole, Qt.ToolTipRole])
            return True
        except Exception as exc:
            self.error.emit(str(exc))
            return False

    def canFetchMore(self, parent: Optional[QModelIndex] = None):
        if parent is not None and hasattr(parent, "isValid") and parent.isValid():
            return False
        return len(self._score_rows) < self._total_score_count

    def fetchMore(self, parent: Optional[QModelIndex] = None):
        if (parent is not None and hasattr(parent, "isValid") and parent.isValid()) or not self.canFetchMore(parent):
            return

        offset = len(self._score_rows)
        try:
            next_scores = self._load_scores(limit=self.page_size, offset=offset)
        except Exception as exc:
            self.error.emit(str(exc))
            return
        if not next_scores:
            return

        first = len(self._folder_rows) + len(self._score_rows)
        last = first + len(next_scores) - 1
        self.beginInsertRows(QModelIndex(), first, last)
        self._score_rows.extend(("score", score) for score in next_scores)
        self.endInsertRows()

    # --------------------------------------------------------------
    # Query lifecycle
    # --------------------------------------------------------------
    @property
    def total_count(self) -> int:
        return self._total_folder_count + self._total_score_count

    @property
    def loaded_count(self) -> int:
        return self.rowCount()

    def set_query(
        self,
        *,
        folder_id: Optional[str],
        scope: str = "folder",
        keyword: str = "",
        sort_column: Optional[int] = None,
        descending: Optional[bool] = None,
    ) -> None:
        if sort_column is not None:
            self.sort_column = int(sort_column)
        if descending is not None:
            self.descending = bool(descending)

        error = None
        self.beginResetModel()
        self.folder_id = folder_id
        self.scope = scope
        self.keyword = keyword.strip()

        try:
            self._folder_rows = []
            self._score_rows = []

            if self.keyword:
                folders = self.manager.search_folders(self.keyword, limit=200)
                self._folder_rows = [("folder", f) for f in folders]
                self._total_folder_count = len(folders)
                self._total_score_count = self.manager.count_search(self.keyword)
            elif self.scope == "favorites":
                self._total_folder_count = 0
                self._total_score_count = self.manager.count_favorites()
            elif self.scope == "recent":
                self._total_folder_count = 0
                self._total_score_count = self.manager.count_recent()
            else:
                folder_sort = "updated_at" if self.sort_column == 5 else "name"
                folders = self.manager.get_child_folders(
                    self.folder_id,
                    sort_by=folder_sort,
                    descending=self.descending if self.sort_column in (0, 5) else False,
                )
                self._folder_rows = [("folder", f) for f in folders]
                self._total_folder_count = len(folders)
                self._total_score_count = self.manager.count_folder_scores(self.folder_id)

            self._score_rows = [
                ("score", score)
                for score in self._load_scores(limit=self.page_size, offset=0)
            ]
        except Exception as exc:
            error = str(exc)
            self._folder_rows = []
            self._score_rows = []
            self._total_folder_count = 0
            self._total_score_count = 0
        finally:
            self.endResetModel()

        if error:
            self.error.emit(error)

    def _score_sort_field(self) -> str:
        return self.SORT_FIELD_BY_COLUMN.get(self.sort_column, "title")

    def _load_scores(self, *, limit: int, offset: int) -> List[ScoreItem]:
        sort_by = self._score_sort_field()
        if self.keyword:
            return self.manager.search(
                self.keyword,
                limit=limit,
                offset=offset,
                sort_by=sort_by,
                descending=self.descending,
            )
        if self.scope == "favorites":
            return self.manager.get_favorites(
                limit=limit,
                offset=offset,
                sort_by=sort_by,
                descending=self.descending,
            )
        if self.scope == "recent":
            # Recent is intentionally chronological, independent of column sorting.
            return self.manager.get_recent(limit=limit, offset=offset)
        return self.manager.get_folder_scores(
            self.folder_id,
            limit=limit,
            offset=offset,
            sort_by=sort_by,
            descending=self.descending,
        )

    def set_sort(self, column: int, descending: bool) -> None:
        self.set_query(
            folder_id=self.folder_id,
            scope=self.scope,
            keyword=self.keyword,
            sort_column=column,
            descending=descending,
        )

    def refresh_current(self) -> None:
        self.set_query(
            folder_id=self.folder_id,
            scope=self.scope,
            keyword=self.keyword,
            sort_column=self.sort_column,
            descending=self.descending,
        )
