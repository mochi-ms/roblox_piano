import os
import shutil
import uuid
import re
from typing import List, Optional
import time

from src.library.models import ScoreItem, FolderItem
from src.library.database import ScoreDatabase
from src.utils.config import ConfigManager
from src.music.timeline import MusicTimeline

class LibraryManager:
    """
    High-level manager for the score library.
    Handles importing files to the library directory and updating the database.
    Now synchronizes the logical folders with physical directories.
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

    def _get_safe_filename(self, target_dir: str, desired_name: str) -> str:
        """
        Returns a conflict-safe filename inside the target directory.
        If 'song.mid' exists, tries 'song (1).mid', etc.
        """
        desired_name = re.sub(r'[\\/*?:"<>|]', "", desired_name)
        base, ext = os.path.splitext(desired_name)
        candidate = desired_name
        counter = 1
        
        while os.path.exists(os.path.join(target_dir, candidate)):
            candidate = f"{base} ({counter}){ext}"
            counter += 1
            
        return candidate

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
        
        # Copy file to library
        shutil.copy2(source_filepath, dest_filepath)
        
        # Default statuses
        status = "READY"
        if ext in [".pdf", ".png", ".jpg", ".jpeg"]:
            status = "ANALYZING"
            
        item = ScoreItem(
            id=score_id,
            title=os.path.splitext(original_filename)[0],
            source_type=source_type,
            source_url=source_filepath,
            filepath=dest_filepath,
            original_filename=original_filename,
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
            self.db.update_score(item)
        
    def register_generated_score(self, xml_filepath: str, title: str, source_type: str, source_url: str, timeline: MusicTimeline, folder_id: Optional[str] = None) -> ScoreItem:
        """
        Registers a newly generated MusicXML or MIDI file into the library.
        Assumes the file is already placed in the library directory or will be moved there.
        """
        score_id = str(uuid.uuid4())
        ext = os.path.splitext(xml_filepath)[1].lower()
        
        target_dir = self._get_folder_path(folder_id)
        os.makedirs(target_dir, exist_ok=True)
        
        # If the generated file isn't in the correct target dir, we should move/copy it.
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

    def get_all(self) -> List[ScoreItem]:
        return self.db.get_all_scores()

    def search(self, keyword: str) -> List[ScoreItem]:
        return self.db.search_scores(keyword)

    def delete_score(self, score_id: str) -> None:
        item = self.db.get_score(score_id)
        if item:
            # Try to remove the file. In reality, send2trash would be better, but standard os.remove for now.
            if os.path.exists(item.filepath):
                try:
                    import send2trash
                    send2trash.send2trash(os.path.normpath(item.filepath))
                except ImportError:
                    try:
                        os.remove(item.filepath)
                    except Exception as e:
                        print(f"Failed to delete file {item.filepath}: {e}")
                except Exception as e:
                    print(f"Failed to send2trash file {item.filepath}: {e}")
            
            # Remove from DB
            self.db.delete_score(score_id)

    def create_folder(self, name: str, parent_id: Optional[str] = None) -> FolderItem:
        # 1. Validate physical path
        parent_path = self._get_folder_path(parent_id)
        safe_name = re.sub(r'[\\/*?:"<>|]', "", name)
        if not safe_name:
            safe_name = "New Folder"
            
        physical_path = os.path.join(parent_path, safe_name)
        # Avoid naming conflicts
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
        
    def get_all_folders(self) -> List[FolderItem]:
        return self.db.get_all_folders()
        
    def get_folder_scores(self, folder_id: Optional[str]) -> List[ScoreItem]:
        scores = self.get_all()
        return [s for s in scores if s.folder_id == folder_id]
        
    def delete_folder(self, folder_id: str) -> None:
        # Recursive delete
        folders = self.get_all_folders()
        children = [f for f in folders if f.parent_id == folder_id]
        for child in children:
            self.delete_folder(child.id)
            
        # Delete scores inside
        scores = self.get_folder_scores(folder_id)
        for s in scores:
            self.db.delete_score(s.id) # Only delete DB row, physical file is handled below
            
        # Delete physical directory to recycle bin
        physical_path = self._get_folder_path(folder_id)
        if os.path.exists(physical_path):
            try:
                import send2trash
                send2trash.send2trash(os.path.normpath(physical_path))
            except ImportError:
                try:
                    shutil.rmtree(physical_path)
                except Exception as e:
                    print(f"Failed to delete folder {physical_path}: {e}")
            except Exception as e:
                print(f"Failed to send2trash folder {physical_path}: {e}")
                
        # Remove from DB
        self.db.delete_folder(folder_id)
        
    def update_folder(self, folder: FolderItem) -> None:
        old_folder = self.db.get_folder(folder.id)
        if not old_folder:
            return
            
        if old_folder.name != folder.name or old_folder.parent_id != folder.parent_id:
            # Physical move/rename required
            old_path = self._get_folder_path(folder.id)
            
            # Temporarily update the object in memory to calculate new path
            temp_name = folder.name
            folder.name = re.sub(r'[\\/*?:"<>|]', "", folder.name)
            if not folder.name:
                folder.name = "Renamed Folder"
                
            new_parent_path = self._get_folder_path(folder.parent_id)
            new_path = os.path.join(new_parent_path, folder.name)
            
            # Avoid conflict
            counter = 1
            original_name = folder.name
            while os.path.exists(new_path) and new_path.lower() != old_path.lower():
                folder.name = f"{original_name} ({counter})"
                new_path = os.path.join(new_parent_path, folder.name)
                counter += 1
                
            if os.path.exists(old_path) and old_path != new_path:
                shutil.move(old_path, new_path)
                
            # Now we must update the filepaths of all scores in this folder and subfolders
            self._update_score_paths_recursive(folder.id, old_path, new_path)
            
        self.db.insert_folder(folder)
        
    def _update_score_paths_recursive(self, folder_id: str, old_base_path: str, new_base_path: str):
        # Update scores in this folder
        scores = self.get_folder_scores(folder_id)
        for s in scores:
            if s.filepath.startswith(old_base_path):
                rel_path = os.path.relpath(s.filepath, old_base_path)
                s.filepath = os.path.join(new_base_path, rel_path)
                self.db.update_score(s)
                
        # Recurse children
        folders = self.get_all_folders()
        children = [f for f in folders if f.parent_id == folder_id]
        for child in children:
            self._update_score_paths_recursive(child.id, old_base_path, new_base_path)

    def move_score(self, score_id: str, new_folder_id: Optional[str]) -> None:
        item = self.db.get_score(score_id)
        if not item: return
        
        if item.folder_id == new_folder_id:
            return
            
        old_path = item.filepath
        target_dir = self._get_folder_path(new_folder_id)
        
        if os.path.exists(old_path):
            filename = os.path.basename(old_path)
            dest_filename = self._get_safe_filename(target_dir, filename)
            new_path = os.path.join(target_dir, dest_filename)
            shutil.move(old_path, new_path)
            item.filepath = new_path
            
        item.folder_id = new_folder_id
        item.updated_at = time.time()
        self.db.update_score(item)
