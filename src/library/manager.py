import os
import shutil
import uuid
from typing import List, Optional
import re

from src.library.models import ScoreItem, FolderItem
from src.library.database import ScoreDatabase
from src.utils.config import ConfigManager
from src.music.timeline import MusicTimeline

class LibraryManager:
    """
    High-level manager for the score library.
    Handles importing files to the library directory and updating the database.
    """
    def __init__(self, config_manager: ConfigManager):
        self.config_manager = config_manager
        self.library_dir = self.config_manager.config.library_dir
        
        # Ensure library directory exists
        os.makedirs(self.library_dir, exist_ok=True)
        
        self.db_path = os.path.join(self.library_dir, "library.db")
        self.db = ScoreDatabase(self.db_path)

    def _get_safe_filename(self, desired_name: str) -> str:
        """
        Returns a conflict-safe filename inside the library directory.
        If 'song.mid' exists, tries 'song (1).mid', etc.
        """
        desired_name = re.sub(r'[\\/*?:"<>|]', "", desired_name)
        base, ext = os.path.splitext(desired_name)
        candidate = desired_name
        counter = 1
        
        while os.path.exists(os.path.join(self.library_dir, candidate)):
            candidate = f"{base} ({counter}){ext}"
            counter += 1
            
        return candidate

    def import_external_file(self, source_filepath: str, source_type: str = "FILE", folder_id: Optional[str] = None) -> ScoreItem:
        """
        Copies an external file into the library and registers it in the database.
        Returns the registered ScoreItem.
        """
        ext = os.path.splitext(source_filepath)[1].lower()
        score_id = str(uuid.uuid4())
        
        original_filename = os.path.basename(source_filepath)
        dest_filename = self._get_safe_filename(original_filename)
        dest_filepath = os.path.join(self.library_dir, dest_filename)
        
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
        
        # If the generated file isn't in the library dir, we should move/copy it.
        if not xml_filepath.startswith(self.library_dir):
            desired_name = f"{title}{ext}" if title else f"generated{ext}"
            dest_filename = self._get_safe_filename(desired_name)
            dest_filepath = os.path.join(self.library_dir, dest_filename)
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
        folder_id = str(uuid.uuid4())
        item = FolderItem(id=folder_id, parent_id=parent_id, name=name)
        self.db.insert_folder(item)
        return item
        
    def get_all_folders(self) -> List[FolderItem]:
        return self.db.get_all_folders()
        
    def delete_folder(self, folder_id: str) -> None:
        self.db.delete_folder(folder_id)
        
    def update_folder(self, folder: FolderItem) -> None:
        self.db.insert_folder(folder)
