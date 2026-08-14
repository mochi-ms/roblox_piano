import os
import shutil
import uuid
from typing import List, Optional

from src.library.models import ScoreItem
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

    def import_external_file(self, source_filepath: str, timeline: MusicTimeline, source_type: str = "FILE") -> ScoreItem:
        """
        Copies an external file into the library and registers it in the database.
        """
        ext = os.path.splitext(source_filepath)[1].lower()
        score_id = str(uuid.uuid4())
        dest_filename = f"{score_id}{ext}"
        dest_filepath = os.path.join(self.library_dir, dest_filename)
        
        # Copy file to library
        shutil.copy2(source_filepath, dest_filepath)
        
        item = ScoreItem(
            id=score_id,
            title=timeline.title if timeline.title else os.path.basename(source_filepath),
            source_type=source_type,
            source_url=source_filepath,
            filepath=dest_filepath,
            duration=timeline.duration,
            bpm=timeline.initial_bpm,
            total_notes=timeline.total_notes,
            tags="imported"
        )
        self.db.insert_score(item)
        return item
        
    def register_generated_score(self, xml_filepath: str, title: str, source_type: str, source_url: str, timeline: MusicTimeline) -> ScoreItem:
        """
        Registers a newly generated MusicXML file (e.g. from YouTube OMR) into the library.
        Assumes the file is already placed in the library directory or will be moved there.
        """
        # If the generated file isn't in the library dir, we should move/copy it.
        if not xml_filepath.startswith(self.library_dir):
            score_id = str(uuid.uuid4())
            dest_filepath = os.path.join(self.library_dir, f"{score_id}.xml")
            shutil.copy2(xml_filepath, dest_filepath)
        else:
            dest_filepath = xml_filepath
            score_id = os.path.splitext(os.path.basename(dest_filepath))[0]
            
        item = ScoreItem(
            id=score_id,
            title=title,
            source_type=source_type,
            source_url=source_url,
            filepath=dest_filepath,
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
            # Try to remove the file
            if os.path.exists(item.filepath):
                try:
                    os.remove(item.filepath)
                except Exception as e:
                    print(f"Failed to delete file {item.filepath}: {e}")
            
            # Remove from DB
            self.db.delete_score(score_id)
