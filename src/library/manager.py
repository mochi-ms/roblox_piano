import os
import re
import shutil
import time
import uuid
from typing import Any, Dict, List, Optional

from src.library.database import ScoreDatabase
from src.library.models import FolderItem, ScoreItem
from src.music.timeline import MusicTimeline
from src.utils.config import ConfigManager


def _send_to_recycle_bin(filepath: str) -> None:
    """Move a file/folder to the Windows Recycle Bin without permanently deleting it."""
    try:
        import send2trash

        send2trash.send2trash(os.path.normpath(filepath))
        return
    except ImportError:
        pass

    import ctypes
    from ctypes import wintypes

    FO_DELETE = 3
    FOF_ALLOWUNDO = 0x0040
    FOF_NOCONFIRMATION = 0x0010
    FOF_SILENT = 0x0004
    FOF_NOERRORUI = 0x0400

    class SHFILEOPSTRUCTW(ctypes.Structure):
        _fields_ = [
            ("hwnd", wintypes.HWND),
            ("wFunc", wintypes.UINT),
            ("pFrom", wintypes.LPCWSTR),
            ("pTo", wintypes.LPCWSTR),
            ("fFlags", wintypes.WORD),
            ("fAnyOperationsAborted", wintypes.BOOL),
            ("hNameMappings", wintypes.LPVOID),
            ("lpszProgressTitle", wintypes.LPCWSTR),
        ]

    abs_path = os.path.abspath(filepath)
    fileop = SHFILEOPSTRUCTW()
    fileop.hwnd = None
    fileop.wFunc = FO_DELETE
    fileop.pFrom = abs_path + "\0\0"
    fileop.pTo = None
    fileop.fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
    fileop.fAnyOperationsAborted = False
    fileop.hNameMappings = None
    fileop.lpszProgressTitle = None

    res = ctypes.windll.shell32.SHFileOperationW(ctypes.byref(fileop))
    if res != 0 or fileop.fAnyOperationsAborted:
        raise OSError(f"SHFileOperation failed to recycle {filepath} with code {res}")


class LibraryManager:
    """
    High-level managed library service.

    Important V1 compatibility rule:
    playback/MIDI/MML code is not changed here. This class only manages the
    library filesystem + SQLite metadata.

    Performance changes versus the previous implementation:
    - folder score queries are SQL-filtered instead of SELECT-all + Python filter;
    - folder path traversal uses a small in-memory folder cache;
    - tree queries use parent-indexed data;
    - startup orphan cleanup no longer performs one full score-table scan per folder;
    - recursive folder import avoids get_all_folders() inside the inner directory loop.
    """

    def __init__(self, config_manager: Any):
        self.config_manager = config_manager
        if hasattr(config_manager, "config"):
            self.library_dir = config_manager.config.library_dir
        elif hasattr(config_manager, "library_dir"):
            self.library_dir = config_manager.library_dir
        else:
            self.library_dir = str(config_manager)
        os.makedirs(self.library_dir, exist_ok=True)

        self.db_path = os.path.join(self.library_dir, "library.db")
        self.db = ScoreDatabase(self.db_path)

        self._folder_cache: Optional[Dict[str, FolderItem]] = None
        self._children_cache: Optional[Dict[Optional[str], List[FolderItem]]] = None

        try:
            self.clean_spurious_empty_folders()
        except Exception as exc:
            print(f"Failed to clean spurious empty folders: {exc}")

    # ------------------------------------------------------------------
    # Folder cache / path resolution
    # ------------------------------------------------------------------
    def _invalidate_folder_cache(self) -> None:
        self._folder_cache = None
        self._children_cache = None

    def _ensure_folder_cache(self) -> None:
        if self._folder_cache is not None and self._children_cache is not None:
            return
        folders = self.db.get_all_folders()
        self._folder_cache = {f.id: f for f in folders}
        children: Dict[Optional[str], List[FolderItem]] = {}
        for folder in folders:
            children.setdefault(folder.parent_id, []).append(folder)
        for values in children.values():
            values.sort(key=lambda f: f.name.casefold())
        self._children_cache = children

    def _get_cached_folder(self, folder_id: Optional[str]) -> Optional[FolderItem]:
        if not folder_id:
            return None
        self._ensure_folder_cache()
        assert self._folder_cache is not None
        return self._folder_cache.get(folder_id)

    def get_folder_path_items(self, folder_id: Optional[str]) -> List[FolderItem]:
        """Return root->current folder objects without repeated SQLite lookups."""
        if not folder_id:
            return []
        self._ensure_folder_cache()
        assert self._folder_cache is not None

        result: List[FolderItem] = []
        visited: set[str] = set()
        current_id: Optional[str] = folder_id
        while current_id and current_id not in visited:
            visited.add(current_id)
            folder = self._folder_cache.get(current_id)
            if not folder:
                break
            result.append(folder)
            current_id = folder.parent_id
        result.reverse()
        return result

    def _get_folder_path(self, folder_id: Optional[str]) -> str:
        if not folder_id:
            return self.library_dir
        return os.path.join(self.library_dir, *(f.name for f in self.get_folder_path_items(folder_id)))

    @staticmethod
    def _sanitize_name(name: str, fallback: str) -> str:
        clean = re.sub(r'[\\/*?:"<>|]', "", name).strip().rstrip(". ")
        return clean or fallback

    def _get_safe_filename(
        self,
        target_dir: str,
        desired_name: str,
        ignore_filepath: Optional[str] = None,
    ) -> str:
        clean_name = self._sanitize_name(desired_name, "Untitled")
        base_name, ext = os.path.splitext(clean_name)
        candidate = clean_name
        counter = 1

        ignore_abs = os.path.abspath(ignore_filepath) if ignore_filepath else None
        while True:
            candidate_path = os.path.join(target_dir, candidate)
            if not os.path.exists(candidate_path):
                return candidate
            if ignore_abs and os.path.abspath(candidate_path) == ignore_abs:
                return candidate
            candidate = f"{base_name} ({counter}){ext}"
            counter += 1

    def _get_safe_folder_name(
        self,
        parent_path: str,
        desired_name: str,
        ignore_path: Optional[str] = None,
    ) -> str:
        clean_name = self._sanitize_name(desired_name, "새 폴더")
        candidate = clean_name
        counter = 1
        ignore_abs = os.path.abspath(ignore_path) if ignore_path else None

        while True:
            candidate_path = os.path.join(parent_path, candidate)
            if not os.path.exists(candidate_path):
                return candidate
            if ignore_abs and os.path.abspath(candidate_path) == ignore_abs:
                return candidate
            candidate = f"{clean_name} ({counter})"
            counter += 1

    # ------------------------------------------------------------------
    # Read/query APIs used by high-performance Library UI
    # ------------------------------------------------------------------
    def get_all(self) -> List[ScoreItem]:
        return self.db.get_all_scores()

    def get_all_folders(self) -> List[FolderItem]:
        self._ensure_folder_cache()
        assert self._folder_cache is not None
        return list(self._folder_cache.values())

    def get_child_folders(
        self,
        parent_id: Optional[str],
        *,
        sort_by: str = "name",
        descending: bool = False,
    ) -> List[FolderItem]:
        # Use DB sort for non-name columns; cached fast path for normal tree use.
        if sort_by != "name":
            return self.db.get_child_folders(parent_id, sort_by=sort_by, descending=descending)
        self._ensure_folder_cache()
        assert self._children_cache is not None
        result = list(self._children_cache.get(parent_id, ()))
        if descending:
            result.reverse()
        return result

    def count_child_folders(self, parent_id: Optional[str]) -> int:
        self._ensure_folder_cache()
        assert self._children_cache is not None
        return len(self._children_cache.get(parent_id, ()))

    def get_folder_scores(
        self,
        folder_id: Optional[str],
        *,
        limit: Optional[int] = None,
        offset: int = 0,
        sort_by: str = "created_at",
        descending: bool = True,
    ) -> List[ScoreItem]:
        return self.db.get_scores_by_folder(
            folder_id,
            limit=limit,
            offset=offset,
            sort_by=sort_by,
            descending=descending,
        )

    def count_folder_scores(self, folder_id: Optional[str]) -> int:
        return self.db.count_scores_by_folder(folder_id)

    def search(
        self,
        keyword: str,
        *,
        limit: Optional[int] = None,
        offset: int = 0,
        sort_by: str = "title",
        descending: bool = False,
    ) -> List[ScoreItem]:
        return self.db.search_scores(
            keyword,
            limit=limit,
            offset=offset,
            sort_by=sort_by,
            descending=descending,
        )

    def count_search(self, keyword: str) -> int:
        return self.db.count_search_scores(keyword)

    def search_folders(self, keyword: str, *, limit: Optional[int] = 200) -> List[FolderItem]:
        return self.db.search_folders(keyword, limit=limit)

    def get_favorites(
        self,
        *,
        limit: Optional[int] = None,
        offset: int = 0,
        sort_by: str = "updated_at",
        descending: bool = True,
    ) -> List[ScoreItem]:
        return self.db.get_favorite_scores(
            limit=limit,
            offset=offset,
            sort_by=sort_by,
            descending=descending,
        )

    def count_favorites(self) -> int:
        return self.db.count_favorite_scores()

    def get_recent(
        self,
        *,
        limit: Optional[int] = None,
        offset: int = 0,
    ) -> List[ScoreItem]:
        return self.db.get_recent_scores(limit=limit, offset=offset)

    def count_recent(self) -> int:
        return self.db.count_recent_scores()

    def toggle_favorite(self, score_id: str) -> bool:
        item = self.db.get_score(score_id)
        if not item:
            raise ValueError(f"Score {score_id} not found")
        new_value = not bool(item.favorite)
        self.db.set_score_favorite(score_id, new_value)
        return new_value

    def mark_played(self, score_id: str) -> None:
        self.db.mark_score_played(score_id, time.time())

    # ------------------------------------------------------------------
    # Import
    # ------------------------------------------------------------------
    def import_folder_recursive(
        self,
        source_folder_path: str,
        target_parent_folder_id: Optional[str] = None,
        progress_callback: Optional[Any] = None,
        cancel_check: Optional[Any] = None,
    ) -> Dict[str, Any]:
        from src.importers.midi_importer import MidiImporter
        from src.importers.musicxml_importer import MusicXmlImporter
        from src.services.mml_service import MmlConversionService

        if not os.path.isdir(source_folder_path):
            raise ValueError(f"Invalid source folder: {source_folder_path}")

        root_name = os.path.basename(os.path.abspath(source_folder_path).rstrip(r"\/")) or "악보"

        # Build a single lookup instead of calling get_all_folders in every loop.
        all_folders = self.db.get_all_folders()
        child_lookup: Dict[tuple[Optional[str], str], FolderItem] = {
            (f.parent_id, f.name.casefold()): f for f in all_folders
        }

        root_folder = child_lookup.get((target_parent_folder_id, root_name.casefold()))
        imported_folders_count = 0
        if root_folder is None:
            root_folder = self.create_folder(root_name, target_parent_folder_id)
            imported_folders_count += 1
            child_lookup[(target_parent_folder_id, root_folder.name.casefold())] = root_folder

        total_files = sum(len(filenames) for _, _, filenames in os.walk(source_folder_path))
        rel_dir_to_folder_id: Dict[str, str] = {"": root_folder.id}

        imported_scores_count = 0
        skipped_count = 0
        failed_count = 0
        failed_items: List[tuple[str, str]] = []
        processed_count = 0
        is_cancelled = False

        midi_imp = MidiImporter()
        xml_imp = MusicXmlImporter()
        mml_svc = MmlConversionService()

        ignore_exts = {".ini", ".db", ".ds_store", ".tmp", ".bak", ".log"}
        score_exts = {
            ".mid",
            ".midi",
            ".musicxml",
            ".mxl",
            ".xml",
            ".mml",
            ".txt",
            ".pdf",
            ".png",
            ".jpg",
            ".jpeg",
        }

        for dirpath, dirnames, filenames in os.walk(source_folder_path):
            if cancel_check and cancel_check():
                is_cancelled = True
                break

            rel_dir = os.path.relpath(dirpath, source_folder_path)
            cur_key = "" if rel_dir == "." else os.path.normpath(rel_dir)
            cur_folder_id = rel_dir_to_folder_id.get(cur_key, root_folder.id)

            for dirname in dirnames:
                sub_key = dirname if not cur_key else os.path.normpath(os.path.join(cur_key, dirname))
                lookup_key = (cur_folder_id, dirname.casefold())
                sub_folder = child_lookup.get(lookup_key)
                if sub_folder is None:
                    sub_folder = self.create_folder(dirname, cur_folder_id)
                    imported_folders_count += 1
                    child_lookup[(cur_folder_id, sub_folder.name.casefold())] = sub_folder
                rel_dir_to_folder_id[sub_key] = sub_folder.id

            target_dir = self._get_folder_path(cur_folder_id)
            os.makedirs(target_dir, exist_ok=True)

            for filename in filenames:
                if cancel_check and cancel_check():
                    is_cancelled = True
                    break

                processed_count += 1
                src_filepath = os.path.join(dirpath, filename)
                lower_name = filename.lower()
                _, ext = os.path.splitext(lower_name)

                if (
                    lower_name in {"desktop.ini", "thumbs.db", ".ds_store"}
                    or lower_name.startswith("readme")
                    or lower_name.startswith("license")
                    or ext in ignore_exts
                    or ext not in score_exts
                ):
                    skipped_count += 1
                    if progress_callback:
                        progress_callback(processed_count, total_files, filename)
                    continue

                source_type = "FILE"
                if ext == ".txt":
                    try:
                        with open(src_filepath, "r", encoding="utf-8", errors="ignore") as file_obj:
                            snippet = file_obj.read(200).strip().upper()
                        if snippet.startswith("MML@"):
                            source_type = "MML"
                        elif re.search(r"\b[1-7][+#-]?\b", snippet):
                            source_type = "NUMERIC"
                        else:
                            skipped_count += 1
                            if progress_callback:
                                progress_callback(processed_count, total_files, filename)
                            continue
                    except Exception:
                        skipped_count += 1
                        if progress_callback:
                            progress_callback(processed_count, total_files, filename)
                        continue
                elif ext in {".mid", ".midi"}:
                    source_type = "MIDI"
                elif ext in {".musicxml", ".mxl", ".xml"}:
                    source_type = "MUSICXML"
                elif ext == ".mml":
                    source_type = "MML"
                elif ext == ".pdf":
                    source_type = "PDF"
                elif ext in {".png", ".jpg", ".jpeg"}:
                    source_type = "IMAGE"

                dest_filename = self._get_safe_filename(target_dir, filename)
                dest_filepath = os.path.join(target_dir, dest_filename)

                try:
                    shutil.copy2(src_filepath, dest_filepath)
                except Exception as exc:
                    failed_count += 1
                    failed_items.append((os.path.join(rel_dir, filename), str(exc)))
                    if progress_callback:
                        progress_callback(processed_count, total_files, filename)
                    continue

                duration = 0.0
                bpm = 120.0
                total_notes_count = 0
                title = os.path.splitext(dest_filename)[0]
                status = "READY"
                error_msg = ""

                try:
                    if source_type == "MIDI":
                        timeline = midi_imp.import_score(dest_filepath)
                        duration = timeline.duration
                        bpm = timeline.initial_bpm
                        total_notes_count = timeline.total_notes
                        if timeline.title:
                            title = timeline.title
                    elif source_type == "MUSICXML":
                        timeline = xml_imp.import_score(dest_filepath)
                        duration = timeline.duration
                        bpm = timeline.initial_bpm
                        total_notes_count = timeline.total_notes
                        if timeline.title:
                            title = timeline.title
                    elif source_type == "MML":
                        with open(dest_filepath, "r", encoding="utf-8", errors="ignore") as mml_file:
                            mml_text = mml_file.read()
                        valid, meta, mml_err, _ = mml_svc.validate_and_analyze(mml_text)
                        if valid and meta:
                            duration = float(meta.get("duration", 0.0))
                            bpm = float(meta.get("bpm", meta.get("tempo", 120.0)))
                            total_notes_count = int(meta.get("notes", meta.get("total_notes", 0)))
                        else:
                            status = "FAILED"
                            error_msg = mml_err or "MML validation failed"
                    elif source_type in {"PDF", "IMAGE"}:
                        status = "ANALYZING"
                except Exception as exc:
                    # Keep the imported file. Metadata failure must not destroy user's copy.
                    error_msg = str(exc)

                score_item = ScoreItem(
                    id=str(uuid.uuid4()),
                    title=title,
                    source_type=source_type,
                    source_url=src_filepath,
                    filepath=dest_filepath,
                    original_filename=dest_filename,
                    file_extension=os.path.splitext(dest_filename)[1].lower(),
                    folder_id=cur_folder_id,
                    duration=duration,
                    bpm=bpm,
                    total_notes=total_notes_count,
                    tags="imported,folder",
                    analysis_status=status,
                    analysis_error=error_msg,
                )
                self.db.insert_score(score_item)
                imported_scores_count += 1

                if progress_callback:
                    progress_callback(processed_count, total_files, filename)

        self._invalidate_folder_cache()
        return {
            "root_folder_id": root_folder.id,
            "root_folder_name": root_folder.name,
            "total_scanned": total_files,
            "imported_folders": imported_folders_count,
            "imported_scores": imported_scores_count,
            "skipped": skipped_count,
            "failed": failed_count,
            "failed_items": failed_items,
            "cancelled": is_cancelled,
        }

    def import_external_file(
        self,
        source_filepath: str,
        source_type: str = "FILE",
        folder_id: Optional[str] = None,
    ) -> ScoreItem:
        ext = os.path.splitext(source_filepath)[1].lower()
        target_dir = self._get_folder_path(folder_id)
        os.makedirs(target_dir, exist_ok=True)

        original_filename = os.path.basename(source_filepath)
        if os.path.abspath(os.path.dirname(source_filepath)) == os.path.abspath(target_dir):
            dest_filename = original_filename
            dest_filepath = source_filepath
        else:
            dest_filename = self._get_safe_filename(target_dir, original_filename)
            dest_filepath = os.path.join(target_dir, dest_filename)
            shutil.copy2(source_filepath, dest_filepath)

        status = "READY"
        duration = 0.0
        bpm = 120.0
        total_notes = 0

        if ext in {".pdf", ".png", ".jpg", ".jpeg"}:
            status = "ANALYZING"
        elif ext in {".mid", ".midi"}:
            try:
                from src.importers.midi_importer import MidiImporter

                timeline = MidiImporter().import_score(dest_filepath)
                duration = timeline.duration
                bpm = timeline.initial_bpm
                total_notes = timeline.total_notes
            except Exception as exc:
                print(f"Could not extract MIDI metadata for {dest_filepath}: {exc}")
        elif ext in {".mml", ".txt"}:
            try:
                from src.importers.mml_importer import MmlImporter

                importer = MmlImporter()
                if importer.can_import(dest_filepath):
                    with open(dest_filepath, "r", encoding="utf-8") as file_obj:
                        meta = importer.extract_metadata(file_obj.read())
                    duration = float(meta.get("duration", 0.0))
                    bpm = float(meta.get("bpm", meta.get("tempo", 120.0)))
                    total_notes = int(meta.get("notes", meta.get("total_notes", 0)))
            except Exception as exc:
                print(f"Could not extract MML metadata for {dest_filepath}: {exc}")
        elif ext in {".musicxml", ".xml", ".mxl"}:
            try:
                from src.importers.musicxml_importer import MusicXmlImporter

                timeline = MusicXmlImporter().import_score(dest_filepath)
                duration = timeline.duration
                bpm = timeline.initial_bpm
                total_notes = timeline.total_notes
            except Exception as exc:
                print(f"Could not extract MusicXML metadata for {dest_filepath}: {exc}")

        item = ScoreItem(
            id=str(uuid.uuid4()),
            title=os.path.splitext(dest_filename)[0],
            source_type=source_type,
            source_url=source_filepath,
            filepath=dest_filepath,
            original_filename=dest_filename,
            file_extension=ext,
            folder_id=folder_id,
            duration=duration,
            bpm=bpm,
            total_notes=total_notes,
            tags="imported",
            analysis_status=status,
        )
        self.db.insert_score(item)
        return item

    def update_score_from_timeline(self, score_id: str, timeline: MusicTimeline) -> None:
        item = self.db.get_score(score_id)
        if not item:
            return
        item.duration = timeline.duration
        item.bpm = timeline.initial_bpm
        item.total_notes = timeline.total_notes
        item.analysis_status = "READY"
        if timeline.title:
            item.title = timeline.title
        item.updated_at = time.time()
        self.db.update_score(item)

    def register_generated_score(
        self,
        xml_filepath: str,
        title: str,
        source_type: str,
        source_url: str,
        timeline: MusicTimeline,
        folder_id: Optional[str] = None,
    ) -> ScoreItem:
        ext = os.path.splitext(xml_filepath)[1].lower()
        target_dir = self._get_folder_path(folder_id)
        os.makedirs(target_dir, exist_ok=True)

        if os.path.dirname(os.path.abspath(xml_filepath)) != os.path.abspath(target_dir):
            desired_name = f"{title}{ext}" if title else f"generated{ext}"
            dest_filename = self._get_safe_filename(target_dir, desired_name)
            dest_filepath = os.path.join(target_dir, dest_filename)
            shutil.copy2(xml_filepath, dest_filepath)
        else:
            dest_filepath = xml_filepath

        item = ScoreItem(
            id=str(uuid.uuid4()),
            title=title,
            source_type=source_type,
            source_url=source_url,
            filepath=dest_filepath,
            original_filename=os.path.basename(dest_filepath),
            file_extension=ext,
            folder_id=folder_id,
            duration=timeline.duration,
            bpm=timeline.initial_bpm,
            total_notes=timeline.total_notes,
            tags="youtube,omr" if source_type == "YOUTUBE" else "omr",
        )
        self.db.insert_score(item)
        return item

    # ------------------------------------------------------------------
    # Score operations
    # ------------------------------------------------------------------
    def rename_score(self, score_id: str, new_name: str) -> ScoreItem:
        item = self.db.get_score(score_id)
        if not item:
            raise ValueError(f"Score {score_id} not found")

        old_filepath = item.filepath
        target_dir = os.path.dirname(old_filepath)

        clean_name = new_name.strip()
        _, new_ext = os.path.splitext(clean_name)
        if not new_ext and item.file_extension:
            clean_name += item.file_extension

        dest_filename = self._get_safe_filename(target_dir, clean_name, ignore_filepath=old_filepath)
        new_filepath = os.path.join(target_dir, dest_filename)

        if os.path.exists(old_filepath) and os.path.abspath(old_filepath) != os.path.abspath(new_filepath):
            shutil.move(old_filepath, new_filepath)

        item.title = os.path.splitext(dest_filename)[0]
        item.filepath = new_filepath
        item.original_filename = dest_filename
        item.updated_at = time.time()
        self.db.update_score(item)
        return item

    def copy_score(self, score_id: str, target_folder_id: Optional[str]) -> ScoreItem:
        item = self.db.get_score(score_id)
        if not item:
            raise ValueError(f"Score {score_id} not found")

        target_dir = self._get_folder_path(target_folder_id)
        os.makedirs(target_dir, exist_ok=True)
        dest_filename = self._get_safe_filename(target_dir, os.path.basename(item.filepath))
        dest_filepath = os.path.join(target_dir, dest_filename)

        if os.path.exists(item.filepath):
            shutil.copy2(item.filepath, dest_filepath)

        copied = ScoreItem(
            id=str(uuid.uuid4()),
            title=os.path.splitext(dest_filename)[0],
            source_type=item.source_type,
            source_url=item.source_url,
            filepath=dest_filepath,
            original_filename=dest_filename,
            file_extension=item.file_extension,
            folder_id=target_folder_id,
            duration=item.duration,
            bpm=item.bpm,
            total_notes=item.total_notes,
            tags=item.tags,
            analysis_status=item.analysis_status,
            analysis_error=item.analysis_error,
            favorite=False,
        )
        self.db.insert_score(copied)
        return copied

    def move_score(self, score_id: str, new_folder_id: Optional[str]) -> None:
        item = self.db.get_score(score_id)
        if not item or item.folder_id == new_folder_id:
            return

        old_path = item.filepath
        target_dir = self._get_folder_path(new_folder_id)
        os.makedirs(target_dir, exist_ok=True)

        if os.path.exists(old_path):
            dest_filename = self._get_safe_filename(target_dir, os.path.basename(old_path))
            new_path = os.path.join(target_dir, dest_filename)
            shutil.move(old_path, new_path)
            item.filepath = new_path
            item.original_filename = dest_filename

        item.folder_id = new_folder_id
        item.updated_at = time.time()
        self.db.update_score(item)

    def delete_score(self, score_id: str, permanent: bool = False) -> None:
        item = self.db.get_score(score_id)
        if not item:
            return

        if os.path.exists(item.filepath):
            if permanent:
                os.remove(item.filepath)
            else:
                _send_to_recycle_bin(item.filepath)

        self.db.delete_score(score_id)

    # ------------------------------------------------------------------
    # Folder operations
    # ------------------------------------------------------------------
    def create_folder(self, name: str, parent_id: Optional[str] = None) -> FolderItem:
        parent_path = self._get_folder_path(parent_id)
        os.makedirs(parent_path, exist_ok=True)
        safe_name = self._get_safe_folder_name(parent_path, name)
        os.makedirs(os.path.join(parent_path, safe_name), exist_ok=True)

        item = FolderItem(id=str(uuid.uuid4()), parent_id=parent_id, name=safe_name)
        self.db.insert_folder(item)
        self._invalidate_folder_cache()
        return item

    def rename_folder(self, folder_id: str, new_name: str) -> FolderItem:
        folder = self.db.get_folder(folder_id)
        if not folder:
            raise ValueError(f"Folder {folder_id} not found")

        old_path = self._get_folder_path(folder.id)
        parent_path = self._get_folder_path(folder.parent_id)
        clean_name = self._get_safe_folder_name(parent_path, new_name, ignore_path=old_path)
        new_path = os.path.join(parent_path, clean_name)

        if os.path.exists(old_path) and os.path.abspath(old_path) != os.path.abspath(new_path):
            shutil.move(old_path, new_path)
            self._update_score_paths_recursive(folder_id, old_path, new_path)

        folder.name = clean_name
        folder.updated_at = time.time()
        self.db.update_folder(folder)
        self._invalidate_folder_cache()
        return folder

    def copy_folder(self, folder_id: str, target_parent_id: Optional[str]) -> FolderItem:
        """
        Explorer-like recursive folder copy using registered library entries.
        Prevent copying a folder into itself/its descendant.
        """
        source = self.db.get_folder(folder_id)
        if not source:
            raise ValueError(f"Folder {folder_id} not found")
        if folder_id == target_parent_id or self.is_descendant(folder_id, target_parent_id):
            raise ValueError("자기 자신이나 하위 폴더 안으로 폴더를 복사할 수 없습니다.")

        def clone(source_folder_id: str, new_parent_id: Optional[str]) -> FolderItem:
            source_folder = self.db.get_folder(source_folder_id)
            if not source_folder:
                raise ValueError(f"Folder {source_folder_id} not found")

            new_folder = self.create_folder(source_folder.name, new_parent_id)
            for score in self.get_folder_scores(source_folder_id):
                self.copy_score(score.id, new_folder.id)
            for child in self.get_child_folders(source_folder_id):
                clone(child.id, new_folder.id)
            return new_folder

        result = clone(folder_id, target_parent_id)
        self._invalidate_folder_cache()
        return result

    def delete_folder(self, folder_id: str, permanent: bool = False) -> None:
        # Snapshot children before mutation so recursive delete is deterministic.
        for child in list(self.get_child_folders(folder_id)):
            self.delete_folder(child.id, permanent=permanent)

        for score in self.get_folder_scores(folder_id):
            self.delete_score(score.id, permanent=permanent)

        physical_path = self._get_folder_path(folder_id)
        if os.path.exists(physical_path):
            if permanent:
                shutil.rmtree(physical_path)
            else:
                _send_to_recycle_bin(physical_path)

        self.db.delete_folder(folder_id)
        self._invalidate_folder_cache()

    def is_descendant(self, parent_folder_id: str, candidate_child_id: Optional[str]) -> bool:
        if not candidate_child_id or not parent_folder_id:
            return False
        if parent_folder_id == candidate_child_id:
            return True

        self._ensure_folder_cache()
        assert self._folder_cache is not None

        current_id: Optional[str] = candidate_child_id
        visited: set[str] = set()
        while current_id and current_id not in visited:
            visited.add(current_id)
            current = self._folder_cache.get(current_id)
            if not current:
                return False
            if current.parent_id == parent_folder_id:
                return True
            current_id = current.parent_id
        return False

    def move_folder(self, folder_id: str, new_parent_id: Optional[str]) -> FolderItem:
        folder = self.db.get_folder(folder_id)
        if not folder:
            raise ValueError(f"Folder {folder_id} not found")
        if folder.parent_id == new_parent_id:
            return folder
        if folder_id == new_parent_id or self.is_descendant(folder_id, new_parent_id):
            raise ValueError("자기 자신이나 하위 폴더로는 이동할 수 없습니다.")

        old_path = self._get_folder_path(folder_id)
        new_parent_path = self._get_folder_path(new_parent_id)
        os.makedirs(new_parent_path, exist_ok=True)
        safe_name = self._get_safe_folder_name(new_parent_path, folder.name, ignore_path=old_path)
        new_path = os.path.join(new_parent_path, safe_name)

        if os.path.exists(old_path) and os.path.abspath(old_path) != os.path.abspath(new_path):
            shutil.move(old_path, new_path)
            self._update_score_paths_recursive(folder_id, old_path, new_path)

        folder.name = safe_name
        folder.parent_id = new_parent_id
        folder.updated_at = time.time()
        self.db.update_folder(folder)
        self._invalidate_folder_cache()
        return folder

    def _update_score_paths_recursive(
        self,
        folder_id: str,
        old_base_path: str,
        new_base_path: str,
    ) -> None:
        for score in self.get_folder_scores(folder_id):
            if score.filepath.startswith(old_base_path):
                rel_path = os.path.relpath(score.filepath, old_base_path)
                score.filepath = os.path.join(new_base_path, rel_path)
                score.updated_at = time.time()
                self.db.update_score(score)

        for child in self.get_child_folders(folder_id):
            self._update_score_paths_recursive(child.id, old_base_path, new_base_path)

    def clean_spurious_empty_folders(self) -> int:
        """
        O(F + S_distinct) cleanup instead of the old O(F * S) implementation.
        Only deletes DB records for folders that:
        - have no registered scores,
        - have no child folders,
        - and no physical directory exists.
        """
        folders = self.db.get_all_folders()
        score_folder_ids = self.db.get_folder_ids_with_scores()
        parent_ids = {f.parent_id for f in folders if f.parent_id is not None}

        folder_map = {f.id: f for f in folders}

        def folder_path(folder_id: str) -> str:
            parts: List[str] = []
            current_id: Optional[str] = folder_id
            visited: set[str] = set()
            while current_id and current_id not in visited:
                visited.add(current_id)
                folder = folder_map.get(current_id)
                if not folder:
                    break
                parts.append(folder.name)
                current_id = folder.parent_id
            return os.path.join(self.library_dir, *reversed(parts))

        cleaned = 0
        for folder in folders:
            if folder.id in score_folder_ids or folder.id in parent_ids:
                continue
            if os.path.exists(folder_path(folder.id)):
                continue
            self.db.delete_folder(folder.id)
            cleaned += 1

        if cleaned:
            self._invalidate_folder_cache()
        return cleaned
