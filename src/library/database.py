import sqlite3
import os
from typing import List, Optional
from src.library.models import ScoreItem, FolderItem


from contextlib import closing

class ScoreDatabase:
    """
    SQLite Database wrapper for Roblox Piano Player library.
    Manages the `scores` table.
    """
    def __init__(self, db_path: str):
        self.db_path = db_path
        self._init_db()

    def _get_connection(self):
        # Enable row_factory to easily map rows to dicts
        conn = sqlite3.connect(self.db_path)
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys = ON")
        return closing(conn)

    def _init_db(self):
        os.makedirs(os.path.dirname(self.db_path), exist_ok=True)
        with self._get_connection() as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS folders (
                    id TEXT PRIMARY KEY,
                    parent_id TEXT,
                    name TEXT NOT NULL,
                    created_at REAL,
                    updated_at REAL DEFAULT 0.0
                )
            """)
            conn.execute("""
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
            """)
            conn.commit()
            self._migrate_db(conn)

    def _migrate_db(self, conn):
        cur = conn.execute("PRAGMA table_info(scores)")
        columns = [row['name'] for row in cur.fetchall()]
        new_columns = {
            'original_filename': "TEXT DEFAULT ''",
            'file_extension': "TEXT DEFAULT ''",
            'folder_id': "TEXT DEFAULT NULL",
            'analysis_status': "TEXT DEFAULT 'READY'",
            'analysis_error': "TEXT DEFAULT ''",
            'favorite': "BOOLEAN DEFAULT 0",
            'updated_at': "REAL DEFAULT 0.0",
            'last_played_at': "REAL DEFAULT 0.0"
        }
        for col, col_type in new_columns.items():
            if col not in columns:
                try:
                    conn.execute(f"ALTER TABLE scores ADD COLUMN {col} {col_type}")
                except Exception as e:
                    print(f"Migration error for column {col}: {e}")
        conn.commit()

    def insert_score(self, score: ScoreItem) -> None:
        with self._get_connection() as conn:
            conn.execute("""
                INSERT OR REPLACE INTO scores 
                (id, title, source_type, source_url, filepath, original_filename, file_extension, folder_id,
                 duration, bpm, total_notes, tags, analysis_status, analysis_error, favorite,
                 created_at, updated_at, last_played_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                score.id, score.title, score.source_type, score.source_url, 
                score.filepath, score.original_filename, score.file_extension, score.folder_id,
                score.duration, score.bpm, score.total_notes, 
                score.tags, score.analysis_status, score.analysis_error, score.favorite,
                score.created_at, score.updated_at, score.last_played_at
            ))
            conn.commit()

    def update_score(self, score: ScoreItem) -> None:
        self.insert_score(score)

    def delete_score(self, score_id: str) -> None:
        with self._get_connection() as conn:
            conn.execute("DELETE FROM scores WHERE id = ?", (score_id,))
            conn.commit()

    def get_score(self, score_id: str) -> Optional[ScoreItem]:
        with self._get_connection() as conn:
            cur = conn.execute("SELECT * FROM scores WHERE id = ?", (score_id,))
            row = cur.fetchone()
            if row:
                return ScoreItem(**dict(row))
        return None

    def get_all_scores(self) -> List[ScoreItem]:
        with self._get_connection() as conn:
            cur = conn.execute("SELECT * FROM scores ORDER BY created_at DESC")
            return [ScoreItem(**dict(row)) for row in cur.fetchall()]

    def search_scores(self, keyword: str) -> List[ScoreItem]:
        like_kw = f"%{keyword}%"
        with self._get_connection() as conn:
            cur = conn.execute("""
                SELECT * FROM scores 
                WHERE title LIKE ? OR tags LIKE ? OR original_filename LIKE ?
                ORDER BY created_at DESC
            """, (like_kw, like_kw, like_kw))
            return [ScoreItem(**dict(row)) for row in cur.fetchall()]
            
    def insert_folder(self, folder: FolderItem) -> None:
        with self._get_connection() as conn:
            conn.execute("""
                INSERT OR REPLACE INTO folders 
                (id, parent_id, name, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?)
            """, (folder.id, folder.parent_id, folder.name, folder.created_at, folder.updated_at))
            conn.commit()

    def update_folder(self, folder: FolderItem) -> None:
        self.insert_folder(folder)

    def get_all_folders(self) -> List[FolderItem]:
        with self._get_connection() as conn:
            cur = conn.execute("SELECT * FROM folders ORDER BY name ASC")
            return [FolderItem(**dict(row)) for row in cur.fetchall()]
            
    def get_folder(self, folder_id: str) -> Optional[FolderItem]:
        with self._get_connection() as conn:
            cur = conn.execute("SELECT * FROM folders WHERE id = ?", (folder_id,))
            row = cur.fetchone()
            if row:
                return FolderItem(**dict(row))
        return None

    def delete_folder(self, folder_id: str) -> None:
        with self._get_connection() as conn:
            conn.execute("DELETE FROM folders WHERE id = ?", (folder_id,))
            # also cascade delete or update children? 
            # The schema doesn't have ON DELETE CASCADE for parent_id but we can manage it in app logic
            conn.commit()
