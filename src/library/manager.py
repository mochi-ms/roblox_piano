import os
import shutil
import uuid
import re
from typing import List, Optional, Tuple, Dict, Any, Callable
import time

from src.library.models import ScoreItem, FolderItem
from src.library.database import ScoreDatabase
from src.utils.config import ConfigManager
from src.music.timeline import MusicTimeline

class LibraryManager:
    """
    High-level manager for the score library.
    Handles importing, renaming, moving, copying, and deleting files and folders,
    synchronizing logical folder structures with physical directories.
    """
    def __init__(self, config_manager: ConfigManager):
        self.config_manager = config_manager
        self.library_dir = self.config_manager.config.library_dir
        
        # Ensure library directory exists
        os.makedirs(self.library_dir, exist_ok=True)
        
        self.db_path = os.path.join(self.library_dir, "library.db")
        self.db = ScoreDatabase(self.db_path)

    def _get_folder_path(self, folder_id: Optional[str]) -> str:
        """Returns the physical absolute path for a folder ID by traversing parents."""
        if not folder_id:
            return self.library_dir
            
        path_parts = []
        current_id = folder_id
        while current_id:
            folder = self.db.get_folder(current_id)
            if not folder:
                break
            path_parts.append(folder.name)
            current_id = folder.parent_id
            
        path_parts.reverse()
        return os.path.join(self.library_dir, *path_parts)

    def _get_safe_filename(self, target_dir: str, desired_name: str, ignore_filepath: Optional[str] = None) -> str:
        """
        Returns a conflict-safe filename inside target_dir.
        If 'song.mid' exists, tries 'song (1).mid', 'song (2).mid', etc.
        """
        clean_name = re.sub(r'[\\/*?:"<>|]', "", desired_name).strip()
        if not clean_name:
            clean_name = "Untitled"
            
        base, ext = os.path.splitext(clean_name)
        candidate = clean_name
        counter = 1
        
        while True:
            full_cand = os.path.join(target_dir, candidate)
            if not os.path.exists(full_cand):
                break
            if ignore_filepath and os.path.abspath(full_cand) == os.path.abspath(ignore_filepath):
                break
            candidate = f"{base} ({counter}){ext}"
            counter += 1
            
        return candidate

    def import_folder_recursive(
        self,
        source_folder_path: str,
        target_parent_folder_id: Optional[str] = None,
        progress_callback: Optional[Any] = None,
        cancel_check: Optional[Any] = None
    ) -> Dict[str, Any]:
        """
        Recursively imports an entire folder hierarchy from the filesystem into the library.
        Preserves original folder tree, file names (with collision safety), and copies files.
        Extracts MusicTimeline metadata for supported score formats.
        """
        from src.importers.midi_importer import MidiImporter
        from src.importers.musicxml_importer import MusicXmlImporter
        from src.importers.mml_importer import MmlImporter
        from src.services.mml_service import MmlConversionService

        if not os.path.exists(source_folder_path) or not os.path.isdir(source_folder_path):
            raise ValueError(f"Invalid source folder: {source_folder_path}")

        root_name = os.path.basename(os.path.abspath(source_folder_path).rstrip(r'\/'))
        if not root_name:
            root_name = "악보"

        # 1. Check or create root folder in target_parent_folder_id
        existing_folders = self.db.get_all_folders()
        root_folder = None
        for f in existing_folders:
            if f.parent_id == target_parent_folder_id and f.name.lower() == root_name.lower():
                root_folder = f
                break

        imported_folders_count = 0
        if not root_folder:
            root_folder = self.create_folder(root_name, target_parent_folder_id)
            imported_folders_count += 1

        # Pre-scan total files for accurate progress
        all_files_to_process = []
        for dirpath, dirnames, filenames in os.walk(source_folder_path):
            for fname in filenames:
                all_files_to_process.append(os.path.join(dirpath, fname))
        total_files = len(all_files_to_process)

        rel_dir_to_folder_id: Dict[str, str] = {"": root_folder.id}
        imported_scores_count = 0
        skipped_count = 0
        failed_count = 0
        failed_items = []
        processed_count = 0
        is_cancelled = False

        midi_imp = MidiImporter()
        xml_imp = MusicXmlImporter()
        mml_svc = MmlConversionService()

        ignore_exts = {".ini", ".db", ".ds_store", ".tmp", ".bak", ".log"}
        score_exts = {".mid", ".midi", ".musicxml", ".mxl", ".xml", ".mml", ".txt", ".pdf", ".png", ".jpg", ".jpeg"}

        for dirpath, dirnames, filenames in os.walk(source_folder_path):
            if cancel_check and cancel_check():
                is_cancelled = True
                break

            rel_dir = os.path.relpath(dirpath, source_folder_path)
            cur_key = "" if rel_dir == "." else os.path.normpath(rel_dir)
            cur_folder_id = rel_dir_to_folder_id.get(cur_key, root_folder.id)

            # Ensure subdirectories are created and mapped
            for d in dirnames:
                sub_key = d if cur_key == "" else os.path.normpath(os.path.join(cur_key, d))
                # Check if folder exists
                sub_folder = None
                for ef in self.db.get_all_folders():
                    if ef.parent_id == cur_folder_id and ef.name.lower() == d.lower():
                        sub_folder = ef
                        break
                if not sub_folder:
                    sub_folder = self.create_folder(d, cur_folder_id)
                    imported_folders_count += 1
                rel_dir_to_folder_id[sub_key] = sub_folder.id

            target_dir = self._get_folder_path(cur_folder_id)
            os.makedirs(target_dir, exist_ok=True)

            # Process files
            for fname in filenames:
                if cancel_check and cancel_check():
                    is_cancelled = True
                    break

                processed_count += 1
                src_filepath = os.path.join(dirpath, fname)
                lower_fname = fname.lower()
                base_name, ext = os.path.splitext(lower_fname)

                # Skip system and ignore files
                if lower_fname in ["desktop.ini", "thumbs.db", ".ds_store"] or lower_fname.startswith("readme") or lower_fname.startswith("license"):
                    skipped_count += 1
                    continue
                if ext in ignore_exts or ext not in score_exts:
                    skipped_count += 1
                    continue

                # Content-based classification for .txt
                source_type = "FILE"
                if ext == ".txt":
                    try:
                        with open(src_filepath, "r", encoding="utf-8", errors="ignore") as f:
                            head_snippet = f.read(100).strip().upper()
                        if head_snippet.startswith("MML@"):
                            source_type = "MML"
                        elif re.search(r'\b[1-7][+#-]?\b', head_snippet):
                            source_type = "NUMERIC"
                        else:
                            skipped_count += 1
                            continue
                    except Exception:
                        skipped_count += 1
                        continue
                elif ext in [".mid", ".midi"]:
                    source_type = "MIDI"
                elif ext in [".musicxml", ".mxl", ".xml"]:
                    source_type = "MUSICXML"
                elif ext == ".mml":
                    source_type = "MML"
                elif ext == ".pdf":
                    source_type = "PDF"
                elif ext in [".png", ".jpg", ".jpeg"]:
                    source_type = "IMAGE"

                # Safe destination filename in target_dir
                dest_filename = self._get_safe_filename(target_dir, fname)
                dest_filepath = os.path.join(target_dir, dest_filename)

                # Copy file physically (preserving original)
                try:
                    shutil.copy2(src_filepath, dest_filepath)
                except Exception as e:
                    failed_count += 1
                    failed_items.append((os.path.join(rel_dir, fname), str(e)))
                    continue

                # Parse metadata via importers
                duration = 0.0
                bpm = 120.0
                total_notes = 0
                title = os.path.splitext(dest_filename)[0]
                status = "READY"
                error_msg = None

                try:
                    if source_type == "MIDI":
                        timeline = midi_imp.import_score(dest_filepath)
                        duration = timeline.duration
                        bpm = timeline.initial_bpm
                        total_notes = timeline.total_notes
                        if timeline.title:
                            title = timeline.title
                    elif source_type == "MUSICXML":
                        timeline = xml_imp.import_score(dest_filepath)
                        duration = timeline.duration
                        bpm = timeline.initial_bpm
                        total_notes = timeline.total_notes
                        if timeline.title:
                            title = timeline.title
                    elif source_type == "MML":
                        with open(dest_filepath, "r", encoding="utf-8", errors="ignore") as f_mml:
                            mml_text = f_mml.read()
                        valid, meta, mml_err, _ = mml_svc.validate_and_analyze(mml_text)
                        if valid:
                            duration = meta.get("duration", 0.0)
                            bpm = meta.get("bpm", 120.0)
                            total_notes = meta.get("notes", 0)
                        else:
                            status = "FAILED"
                            error_msg = mml_err
                    elif source_type in ["PDF", "IMAGE"]:
                        status = "ANALYZING"
                except Exception as ex:
                    # Keep file registered but flag status
                    status = "READY"
                    error_msg = str(ex)

                score_id = str(uuid.uuid4())
                score_item = ScoreItem(
                    id=score_id,
                    title=title,
                    source_type=source_type,
                    source_url=src_filepath,
                    filepath=dest_filepath,
                    original_filename=dest_filename,
                    file_extension=os.path.splitext(dest_filename)[1].lower(),
                    folder_id=cur_folder_id,
                    duration=duration,
                    bpm=bpm,
                    total_notes=total_notes,
                    tags="imported,folder",
                    analysis_status=status,
                    analysis_error=error_msg
                )
                self.db.insert_score(score_item)
                imported_scores_count += 1

                if progress_callback:
                    progress_callback(processed_count, total_files, fname)

        summary = {
            "root_folder_id": root_folder.id,
            "root_folder_name": root_folder.name,
            "total_scanned": total_files,
            "imported_folders": imported_folders_count,
            "imported_scores": imported_scores_count,
            "skipped": skipped_count,
            "failed": failed_count,
            "failed_items": failed_items,
            "cancelled": is_cancelled
        }
        return summary

    def import_external_file(self, source_filepath: str, source_type: str = "FILE", folder_id: Optional[str] = None) -> ScoreItem:
        """
        Copies an external file into the appropriate physical folder and registers it in the database.
        Returns the registered ScoreItem.
        """
        ext = os.path.splitext(source_filepath)[1].lower()
        score_id = str(uuid.uuid4())
        
        target_dir = self._get_folder_path(folder_id)
        os.makedirs(target_dir, exist_ok=True)
        
        original_filename = os.path.basename(source_filepath)
        dest_filename = self._get_safe_filename(target_dir, original_filename)
        dest_filepath = os.path.join(target_dir, dest_filename)
        
        # Copy file to library directory
        shutil.copy2(source_filepath, dest_filepath)
        
        # Default statuses
        status = "READY"
        if ext in [".pdf", ".png", ".jpg", ".jpeg"]:
            status = "ANALYZING"
            
        item = ScoreItem(
            id=score_id,
            title=os.path.splitext(dest_filename)[0],
            source_type=source_type,
            source_url=source_filepath,
            filepath=dest_filepath,
            original_filename=dest_filename,
            file_extension=ext,
            folder_id=folder_id,
            duration=0.0,
            bpm=120.0,
            total_notes=0,
            tags="imported",
            analysis_status=status
        )
        self.db.insert_score(item)
        return item
        
    def update_score_from_timeline(self, score_id: str, timeline: MusicTimeline) -> None:
        """Updates duration, bpm, total_notes, and status when timeline is parsed successfully."""
        item = self.db.get_score(score_id)
        if item:
            item.duration = timeline.duration
            item.bpm = timeline.initial_bpm
            item.total_notes = timeline.total_notes
            item.analysis_status = "READY"
            if timeline.title:
                item.title = timeline.title
            item.updated_at = time.time()
            self.db.update_score(item)
            
    def register_generated_score(self, xml_filepath: str, title: str, source_type: str, source_url: str, timeline: MusicTimeline, folder_id: Optional[str] = None) -> ScoreItem:
        """Registers a newly generated MusicXML or MIDI file into the library."""
        score_id = str(uuid.uuid4())
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
            id=score_id,
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
            tags="youtube,omr" if source_type == "YOUTUBE" else "omr"
        )
        self.db.insert_score(item)
        return item
        
    def rename_score(self, score_id: str, new_name: str) -> ScoreItem:
        """
        Safely renames a score item physically and in the database.
        Maintains the file extension unless specified.
        """
        item = self.db.get_score(score_id)
        if not item:
            raise ValueError(f"Score {score_id} not found")
            
        old_filepath = item.filepath
        target_dir = os.path.dirname(old_filepath)
        
        # Handle extension
        clean_name = new_name.strip()
        _, new_ext = os.path.splitext(clean_name)
        if not new_ext and item.file_extension:
            clean_name = f"{clean_name}{item.file_extension}"
            
        dest_filename = self._get_safe_filename(target_dir, clean_name, ignore_filepath=old_filepath)
        new_filepath = os.path.join(target_dir, dest_filename)
        
        # Physical rename
        if os.path.exists(old_filepath) and os.path.abspath(old_filepath) != os.path.abspath(new_filepath):
            shutil.move(old_filepath, new_filepath)
            
        item.title = os.path.splitext(dest_filename)[0]
        item.filepath = new_filepath
        item.original_filename = dest_filename
        item.updated_at = time.time()
        self.db.update_score(item)
        return item

    def copy_score(self, score_id: str, target_folder_id: Optional[str]) -> ScoreItem:
        """
        Copies a score to target_folder_id, creating a new ScoreItem in DB.
        """
        item = self.db.get_score(score_id)
        if not item:
            raise ValueError(f"Score {score_id} not found")
            
        target_dir = self._get_folder_path(target_folder_id)
        os.makedirs(target_dir, exist_ok=True)
        
        filename = os.path.basename(item.filepath)
        dest_filename = self._get_safe_filename(target_dir, filename)
        dest_filepath = os.path.join(target_dir, dest_filename)
        
        if os.path.exists(item.filepath):
            shutil.copy2(item.filepath, dest_filepath)
            
        new_item = ScoreItem(
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
            analysis_status=item.analysis_status
        )
        self.db.insert_score(new_item)
        return new_item

    def move_score(self, score_id: str, new_folder_id: Optional[str]) -> None:
        """Moves a score physically and updates DB."""
        item = self.db.get_score(score_id)
        if not item:
            return
        
        if item.folder_id == new_folder_id:
            return
            
        old_path = item.filepath
        target_dir = self._get_folder_path(new_folder_id)
        os.makedirs(target_dir, exist_ok=True)
        
        if os.path.exists(old_path):
            filename = os.path.basename(old_path)
            dest_filename = self._get_safe_filename(target_dir, filename)
            new_path = os.path.join(target_dir, dest_filename)
            shutil.move(old_path, new_path)
            item.filepath = new_path
            item.original_filename = dest_filename
            
        item.folder_id = new_folder_id
        item.updated_at = time.time()
        self.db.update_score(item)

    def delete_score(self, score_id: str, permanent: bool = False) -> None:
        """Deletes a score item from disk (Recycle Bin or permanent) and removes from DB."""
        item = self.db.get_score(score_id)
        if item:
            if os.path.exists(item.filepath):
                if permanent:
                    try:
                        os.remove(item.filepath)
                    except Exception as e:
                        print(f"Failed to permanently delete {item.filepath}: {e}")
                else:
                    try:
                        import send2trash
                        send2trash.send2trash(os.path.normpath(item.filepath))
                    except Exception:
                        try:
                            os.remove(item.filepath)
                        except Exception as e:
                            print(f"Failed to delete {item.filepath}: {e}")
            self.db.delete_score(score_id)

    def create_folder(self, name: str, parent_id: Optional[str] = None) -> FolderItem:
        """Creates a new folder physically and in DB."""
        parent_path = self._get_folder_path(parent_id)
        safe_name = re.sub(r'[\\/*?:"<>|]', "", name).strip()
        if not safe_name:
            safe_name = "새 폴더"
            
        physical_path = os.path.join(parent_path, safe_name)
        original_safe = safe_name
        counter = 1
        while os.path.exists(physical_path):
            safe_name = f"{original_safe} ({counter})"
            physical_path = os.path.join(parent_path, safe_name)
            counter += 1

        os.makedirs(physical_path, exist_ok=True)
        
        folder_id = str(uuid.uuid4())
        item = FolderItem(id=folder_id, parent_id=parent_id, name=safe_name)
        self.db.insert_folder(item)
        return item

    def rename_folder(self, folder_id: str, new_name: str) -> FolderItem:
        """Renames a folder and moves directory if necessary."""
        folder = self.db.get_folder(folder_id)
        if not folder:
            raise ValueError(f"Folder {folder_id} not found")
            
        old_path = self._get_folder_path(folder.id)
        clean_name = re.sub(r'[\\/*?:"<>|]', "", new_name).strip()
        if not clean_name:
            clean_name = "새 폴더"
            
        parent_path = self._get_folder_path(folder.parent_id)
        new_path = os.path.join(parent_path, clean_name)
        
        counter = 1
        orig_cand = clean_name
        while os.path.exists(new_path) and os.path.abspath(new_path).lower() != os.path.abspath(old_path).lower():
            clean_name = f"{orig_cand} ({counter})"
            new_path = os.path.join(parent_path, clean_name)
            counter += 1
            
        if os.path.exists(old_path) and os.path.abspath(old_path) != os.path.abspath(new_path):
            shutil.move(old_path, new_path)
            self._update_score_paths_recursive(folder_id, old_path, new_path)
            
        folder.name = clean_name
        folder.updated_at = time.time()
        self.db.update_folder(folder)
        return folder

    def delete_folder(self, folder_id: str, permanent: bool = False) -> None:
        """Recursively deletes a folder and scores inside."""
        folders = self.get_all_folders()
        children = [f for f in folders if f.parent_id == folder_id]
        for child in children:
            self.delete_folder(child.id, permanent=permanent)
            
        scores = self.get_folder_scores(folder_id)
        for s in scores:
            self.db.delete_score(s.id)
            
        physical_path = self._get_folder_path(folder_id)
        if os.path.exists(physical_path):
            if permanent:
                try:
                    shutil.rmtree(physical_path)
                except Exception as e:
                    print(f"Failed to delete folder {physical_path}: {e}")
            else:
                try:
                    import send2trash
                    send2trash.send2trash(os.path.normpath(physical_path))
                except Exception:
                    try:
                        shutil.rmtree(physical_path)
                    except Exception as e:
                        print(f"Failed to delete folder {physical_path}: {e}")
                        
        self.db.delete_folder(folder_id)

    def _update_score_paths_recursive(self, folder_id: str, old_base_path: str, new_base_path: str):
        scores = self.get_folder_scores(folder_id)
        for s in scores:
            if s.filepath.startswith(old_base_path):
                rel_path = os.path.relpath(s.filepath, old_base_path)
                s.filepath = os.path.join(new_base_path, rel_path)
                self.db.update_score(s)
                
        folders = self.get_all_folders()
        children = [f for f in folders if f.parent_id == folder_id]
        for child in children:
            self._update_score_paths_recursive(child.id, old_base_path, new_base_path)

    def get_all(self) -> List[ScoreItem]:
        return self.db.get_all_scores()

    def search(self, keyword: str) -> List[ScoreItem]:
        return self.db.search_scores(keyword)

    def get_all_folders(self) -> List[FolderItem]:
        return self.db.get_all_folders()
        
    def get_folder_scores(self, folder_id: Optional[str]) -> List[ScoreItem]:
        scores = self.get_all()
        return [s for s in scores if s.folder_id == folder_id]
