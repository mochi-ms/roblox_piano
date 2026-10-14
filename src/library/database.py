import os
import sqlite3
from contextlib import closing
from typing import List, Optional

from src.library.models import FolderItem, ScoreItem


class ScoreDatabase:
    """
    SQLite data access for the score library.

    Performance principles:
    - keep DB filtering/sorting inside SQLite;
    - never load the entire score table just to display one folder;
    - keep connections short-lived so QThread workers remain safe;
    - use WAL + targeted indexes for read-heavy library browsing.
    """

    _SCORE_SORT_COLUMNS = {
        "title": "title COLLATE NOCASE",
        "file_extension": "file_extension COLLATE NOCASE",
        "duration": "duration",
        "bpm": "bpm",
        "total_notes": "total_notes",
        "created_at": "created_at",
        "updated_at": "CASE WHEN updated_at > 0 THEN updated_at ELSE created_at END",
        "last_played_at": "last_played_at",
    }

    _FOLDER_SORT_COLUMNS = {
        "name": "name COLLATE NOCASE",
        "created_at": "created_at",
        "updated_at": "CASE WHEN updated_at > 0 THEN updated_at ELSE created_at END",
    }

    def __init__(self, db_path: str):
        self.db_path = db_path
        self._init_db()

    def _get_connection(self):
        conn = sqlite3.connect(self.db_path, timeout=10.0)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys = ON")
        conn.execute("PRAGMA busy_timeout = 5000")
        return closing(conn)

    def _init_db(self):
        parent = os.path.dirname(os.path.abspath(self.db_path))
        if parent:
            os.makedirs(parent, exist_ok=True)

        with self._get_connection() as conn:
            # WAL greatly reduces read/write contention between UI and import workers.
            try:
                conn.execute("PRAGMA journal_mode = WAL")
            except sqlite3.DatabaseError:
                pass
            conn.execute("PRAGMA synchronous = NORMAL")
            conn.execute("PRAGMA temp_store = MEMORY")

            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS folders (
                    id TEXT PRIMARY KEY,
                    parent_id TEXT,
                    name TEXT NOT NULL,
                    created_at REAL,
                    updated_at REAL DEFAULT 0.0
                )
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS scores (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    source_type TEXT,
                    source_url TEXT,
                    filepath TEXT NOT NULL,
                    original_filename TEXT DEFAULT '',
                    file_extension TEXT DEFAULT '',
                    folder_id TEXT DEFAULT NULL,
                    duration REAL DEFAULT 0.0,
                    bpm REAL DEFAULT 120.0,
                    total_notes INTEGER DEFAULT 0,
                    tags TEXT DEFAULT '',
                    analysis_status TEXT DEFAULT 'READY',
                    analysis_error TEXT DEFAULT '',
                    favorite BOOLEAN DEFAULT 0,
                    created_at REAL,
                    updated_at REAL DEFAULT 0.0,
                    last_played_at REAL DEFAULT 0.0,
                    FOREIGN KEY(folder_id) REFERENCES folders(id) ON DELETE SET NULL
                )
                """
            )
            self._migrate_db(conn)
            self._ensure_indexes(conn)
            conn.commit()

    def _migrate_db(self, conn):
        cur = conn.execute("PRAGMA table_info(scores)")
        columns = [row["name"] for row in cur.fetchall()]
        new_columns = {
            "original_filename": "TEXT DEFAULT ''",
            "file_extension": "TEXT DEFAULT ''",
            "folder_id": "TEXT DEFAULT NULL",
            "analysis_status": "TEXT DEFAULT 'READY'",
            "analysis_error": "TEXT DEFAULT ''",
            "favorite": "BOOLEAN DEFAULT 0",
            "updated_at": "REAL DEFAULT 0.0",
            "last_played_at": "REAL DEFAULT 0.0",
        }
        for col, col_type in new_columns.items():
            if col not in columns:
                conn.execute(f"ALTER TABLE scores ADD COLUMN {col} {col_type}")

    def _ensure_indexes(self, conn):
        # These match the actual Library WHERE / ORDER BY patterns.
        conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_scores_folder_created "
            "ON scores(folder_id, created_at DESC)"
        )
        conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_scores_folder_updated "
            "ON scores(folder_id, updated_at DESC)"
        )
        conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_scores_folder_title "
            "ON scores(folder_id, title COLLATE NOCASE)"
        )
        conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_scores_favorite_updated "
            "ON scores(favorite, updated_at DESC)"
        )
        conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_scores_last_played "
            "ON scores(last_played_at DESC)"
        )
        conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_folders_parent_name "
            "ON folders(parent_id, name COLLATE NOCASE)"
        )

    @staticmethod
    def _limit_clause(limit: Optional[int], offset: int) -> tuple[str, list]:
        if limit is None:
            return "", []
        return " LIMIT ? OFFSET ? ", [max(0, int(limit)), max(0, int(offset))]

    @classmethod
    def _score_order_clause(cls, sort_by: str, descending: bool) -> str:
        col = cls._SCORE_SORT_COLUMNS.get(sort_by, cls._SCORE_SORT_COLUMNS["created_at"])
        return f" ORDER BY {col} {'DESC' if descending else 'ASC'}, id ASC "

    @classmethod
    def _folder_order_clause(cls, sort_by: str, descending: bool) -> str:
        col = cls._FOLDER_SORT_COLUMNS.get(sort_by, cls._FOLDER_SORT_COLUMNS["name"])
        return f" ORDER BY {col} {'DESC' if descending else 'ASC'}, id ASC "

    @staticmethod
    def _folder_filter(folder_id: Optional[str]) -> tuple[str, list]:
        if folder_id is None:
            return "folder_id IS NULL", []
        return "folder_id = ?", [folder_id]

    # ------------------------------------------------------------------
    # Score CRUD
    # ------------------------------------------------------------------
    def insert_score(self, score: ScoreItem) -> None:
        with self._get_connection() as conn:
            conn.execute(
                """
                INSERT OR REPLACE INTO scores
                (id, title, source_type, source_url, filepath, original_filename, file_extension, folder_id,
                 duration, bpm, total_notes, tags, analysis_status, analysis_error, favorite,
                 created_at, updated_at, last_played_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    score.id,
                    score.title,
                    score.source_type,
                    score.source_url,
                    score.filepath,
                    score.original_filename,
                    score.file_extension,
                    score.folder_id,
                    score.duration,
                    score.bpm,
                    score.total_notes,
                    score.tags,
                    score.analysis_status,
                    score.analysis_error,
                    bool(score.favorite),
                    score.created_at,
                    score.updated_at,
                    score.last_played_at,
                ),
            )
            conn.commit()

    def update_score(self, score: ScoreItem) -> None:
        self.insert_score(score)

    def delete_score(self, score_id: str) -> None:
        with self._get_connection() as conn:
            conn.execute("DELETE FROM scores WHERE id = ?", (score_id,))
            conn.commit()

    def get_score(self, score_id: str) -> Optional[ScoreItem]:
        with self._get_connection() as conn:
            row = conn.execute("SELECT * FROM scores WHERE id = ?", (score_id,)).fetchone()
            return ScoreItem(**dict(row)) if row else None

    def get_all_scores(self) -> List[ScoreItem]:
        # Compatibility API. UI code should prefer paged/query methods.
        with self._get_connection() as conn:
            rows = conn.execute("SELECT * FROM scores ORDER BY created_at DESC").fetchall()
            return [ScoreItem(**dict(row)) for row in rows]

    def get_scores_by_folder(
        self,
        folder_id: Optional[str],
        *,
        limit: Optional[int] = None,
        offset: int = 0,
        sort_by: str = "created_at",
        descending: bool = True,
    ) -> List[ScoreItem]:
        where_sql, params = self._folder_filter(folder_id)
        limit_sql, limit_params = self._limit_clause(limit, offset)
        sql = (
            f"SELECT * FROM scores WHERE {where_sql}"
            + self._score_order_clause(sort_by, descending)
            + limit_sql
        )
        with self._get_connection() as conn:
            rows = conn.execute(sql, [*params, *limit_params]).fetchall()
            return [ScoreItem(**dict(row)) for row in rows]

    def count_scores_by_folder(self, folder_id: Optional[str]) -> int:
        where_sql, params = self._folder_filter(folder_id)
        with self._get_connection() as conn:
            row = conn.execute(f"SELECT COUNT(*) AS c FROM scores WHERE {where_sql}", params).fetchone()
            return int(row["c"])

    def search_scores(
        self,
        keyword: str,
        *,
        limit: Optional[int] = None,
        offset: int = 0,
        sort_by: str = "title",
        descending: bool = False,
    ) -> List[ScoreItem]:
        kw = keyword.strip()
        if not kw:
            return self.get_all_scores() if limit is None else self.get_scores_page(
                limit=limit, offset=offset, sort_by=sort_by, descending=descending
            )

        like_kw = f"%{kw}%"
        limit_sql, limit_params = self._limit_clause(limit, offset)
        sql = (
            """
            SELECT * FROM scores
            WHERE title LIKE ? COLLATE NOCASE
               OR tags LIKE ? COLLATE NOCASE
               OR original_filename LIKE ? COLLATE NOCASE
            """
            + self._score_order_clause(sort_by, descending)
            + limit_sql
        )
        with self._get_connection() as conn:
            rows = conn.execute(sql, [like_kw, like_kw, like_kw, *limit_params]).fetchall()
            return [ScoreItem(**dict(row)) for row in rows]

    def count_search_scores(self, keyword: str) -> int:
        kw = keyword.strip()
        with self._get_connection() as conn:
            if not kw:
                row = conn.execute("SELECT COUNT(*) AS c FROM scores").fetchone()
            else:
                like_kw = f"%{kw}%"
                row = conn.execute(
                    """
                    SELECT COUNT(*) AS c FROM scores
                    WHERE title LIKE ? COLLATE NOCASE
                       OR tags LIKE ? COLLATE NOCASE
                       OR original_filename LIKE ? COLLATE NOCASE
                    """,
                    (like_kw, like_kw, like_kw),
                ).fetchone()
            return int(row["c"])

    def get_scores_page(
        self,
        *,
        limit: int,
        offset: int = 0,
        sort_by: str = "created_at",
        descending: bool = True,
    ) -> List[ScoreItem]:
        limit_sql, limit_params = self._limit_clause(limit, offset)
        sql = "SELECT * FROM scores" + self._score_order_clause(sort_by, descending) + limit_sql
        with self._get_connection() as conn:
            rows = conn.execute(sql, limit_params).fetchall()
            return [ScoreItem(**dict(row)) for row in rows]

    def get_favorite_scores(
        self,
        *,
        limit: Optional[int] = None,
        offset: int = 0,
        sort_by: str = "updated_at",
        descending: bool = True,
    ) -> List[ScoreItem]:
        limit_sql, limit_params = self._limit_clause(limit, offset)
        sql = (
            "SELECT * FROM scores WHERE favorite = 1"
            + self._score_order_clause(sort_by, descending)
            + limit_sql
        )
        with self._get_connection() as conn:
            rows = conn.execute(sql, limit_params).fetchall()
            return [ScoreItem(**dict(row)) for row in rows]

    def count_favorite_scores(self) -> int:
        with self._get_connection() as conn:
            return int(conn.execute("SELECT COUNT(*) FROM scores WHERE favorite = 1").fetchone()[0])

    def get_recent_scores(
        self,
        *,
        limit: Optional[int] = None,
        offset: int = 0,
    ) -> List[ScoreItem]:
        limit_sql, limit_params = self._limit_clause(limit, offset)
        sql = (
            "SELECT * FROM scores WHERE last_played_at > 0 "
            "ORDER BY last_played_at DESC, id ASC"
            + limit_sql
        )
        with self._get_connection() as conn:
            rows = conn.execute(sql, limit_params).fetchall()
            return [ScoreItem(**dict(row)) for row in rows]

    def count_recent_scores(self) -> int:
        with self._get_connection() as conn:
            return int(conn.execute("SELECT COUNT(*) FROM scores WHERE last_played_at > 0").fetchone()[0])

    def set_score_favorite(self, score_id: str, favorite: bool) -> None:
        with self._get_connection() as conn:
            conn.execute(
                "UPDATE scores SET favorite = ?, updated_at = strftime('%s','now') WHERE id = ?",
                (1 if favorite else 0, score_id),
            )
            conn.commit()

    def mark_score_played(self, score_id: str, timestamp: float) -> None:
        with self._get_connection() as conn:
            conn.execute("UPDATE scores SET last_played_at = ? WHERE id = ?", (timestamp, score_id))
            conn.commit()

    def get_folder_ids_with_scores(self) -> set[str]:
        with self._get_connection() as conn:
            rows = conn.execute(
                "SELECT DISTINCT folder_id FROM scores WHERE folder_id IS NOT NULL"
            ).fetchall()
            return {str(row["folder_id"]) for row in rows}

    # ------------------------------------------------------------------
    # Folder CRUD / queries
    # ------------------------------------------------------------------
    def insert_folder(self, folder: FolderItem) -> None:
        with self._get_connection() as conn:
            conn.execute(
                """
                INSERT OR REPLACE INTO folders
                (id, parent_id, name, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?)
                """,
                (folder.id, folder.parent_id, folder.name, folder.created_at, folder.updated_at),
            )
            conn.commit()

    def update_folder(self, folder: FolderItem) -> None:
        self.insert_folder(folder)

    def get_all_folders(self) -> List[FolderItem]:
        with self._get_connection() as conn:
            rows = conn.execute("SELECT * FROM folders ORDER BY name COLLATE NOCASE ASC").fetchall()
            return [FolderItem(**dict(row)) for row in rows]

    def get_child_folders(
        self,
        parent_id: Optional[str],
        *,
        sort_by: str = "name",
        descending: bool = False,
    ) -> List[FolderItem]:
        if parent_id is None:
            where_sql, params = "parent_id IS NULL", []
        else:
            where_sql, params = "parent_id = ?", [parent_id]
        sql = (
            f"SELECT * FROM folders WHERE {where_sql}"
            + self._folder_order_clause(sort_by, descending)
        )
        with self._get_connection() as conn:
            rows = conn.execute(sql, params).fetchall()
            return [FolderItem(**dict(row)) for row in rows]

    def count_child_folders(self, parent_id: Optional[str]) -> int:
        if parent_id is None:
            where_sql, params = "parent_id IS NULL", []
        else:
            where_sql, params = "parent_id = ?", [parent_id]
        with self._get_connection() as conn:
            row = conn.execute(
                f"SELECT COUNT(*) AS c FROM folders WHERE {where_sql}", params
            ).fetchone()
            return int(row["c"])

    def search_folders(self, keyword: str, *, limit: Optional[int] = 200) -> List[FolderItem]:
        kw = keyword.strip()
        if not kw:
            return []
        limit_sql, limit_params = self._limit_clause(limit, 0)
        sql = (
            "SELECT * FROM folders WHERE name LIKE ? COLLATE NOCASE "
            "ORDER BY name COLLATE NOCASE ASC"
            + limit_sql
        )
        with self._get_connection() as conn:
            rows = conn.execute(sql, [f"%{kw}%", *limit_params]).fetchall()
            return [FolderItem(**dict(row)) for row in rows]

    def get_folder(self, folder_id: str) -> Optional[FolderItem]:
        with self._get_connection() as conn:
            row = conn.execute("SELECT * FROM folders WHERE id = ?", (folder_id,)).fetchone()
            return FolderItem(**dict(row)) if row else None

    def delete_folder(self, folder_id: str) -> None:
        with self._get_connection() as conn:
            conn.execute("DELETE FROM folders WHERE id = ?", (folder_id,))
            conn.commit()

    # ------------------------------------------------------------------
    # Diagnostics used by tests / profiling
    # ------------------------------------------------------------------
    def get_index_names(self) -> set[str]:
        with self._get_connection() as conn:
            rows = conn.execute(
                "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'"
            ).fetchall()
            return {str(row["name"]) for row in rows}
