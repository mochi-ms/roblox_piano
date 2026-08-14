import os
import tempfile
import mido
import re
from typing import Dict, Any, Tuple, Optional

from src.importers.mml_importer import MmlImporter, MmlParseError
from src.importers.midi_importer import MidiImporter
from src.library.manager import LibraryManager
from src.library.models import ScoreItem
from src.music.timeline import MusicTimeline

class MmlConversionService:
    """
    Dedicated service for validating, analyzing, exporting, and importing MML scores.
    Guarantees physical file integrity and strict validation before library insertion.
    """
    def __init__(self):
        self.importer = MmlImporter()
        self.midi_importer = MidiImporter()

    def sanitize_title(self, title: str, default: str = "새 MML 악보") -> str:
        """Sanitizes file title for Windows file system safety."""
        title = title.strip()
        if not title:
            return default
        # Remove invalid chars: \ / : * ? " < > |
        clean = re.sub(r'[\\/*?:"<>|]', '', title).strip()
        # Reserved DOS names
        reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                    "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
                    "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"]
        if clean.upper() in reserved:
            clean = f"Score_{clean}"
        return clean if clean else default

    def validate_and_analyze(self, mml_text: str) -> Tuple[bool, Optional[Dict[str, Any]], Optional[str], Optional[int]]:
        """
        Validates MML text and extracts metadata.
        Returns (is_valid, metadata_dict, error_message, error_position)
        """
        text = mml_text.strip()
        if not text:
            return False, None, "MML 코드를 입력해 주세요.", None
        try:
            stats = self.importer.extract_metadata(text)
            return True, stats, None, None
        except MmlParseError as e:
            msg = f"Track {e.track_idx + 1}, 위치 {e.position}: {e.custom_message or '구문 오류'}"
            return False, None, msg, e.position
        except Exception as e:
            return False, None, f"MML 파싱 오류: {str(e)}", None

    def convert_to_midi_file(self, mml_text: str, out_path: str) -> Dict[str, Any]:
        """
        Converts MML to MIDI and saves it to out_path, verifying output with mido.
        """
        self.importer.convert_to_midi(mml_text, out_path)
        
        if not os.path.exists(out_path) or os.path.getsize(out_path) == 0:
            raise IOError(f"MIDI 파일 생성 실패: {out_path}")
            
        # Verify reopening
        mid = mido.MidiFile(out_path)
        total_notes = 0
        for track in mid.tracks:
            for msg in track:
                if msg.type == 'note_on' and msg.velocity > 0:
                    total_notes += 1
                    
        return {
            "file_size": os.path.getsize(out_path),
            "tracks": len(mid.tracks),
            "note_count": total_notes,
            "duration": mid.length
        }

    def export_to_file(self, mml_text: str, dest_filepath: str) -> Dict[str, Any]:
        """
        Exports MML to user-specified destination filepath.
        """
        parent_dir = os.path.dirname(os.path.abspath(dest_filepath))
        if parent_dir:
            os.makedirs(parent_dir, exist_ok=True)
            
        return self.convert_to_midi_file(mml_text, dest_filepath)

    def import_to_library(
        self,
        mml_text: str,
        title: str,
        folder_id: Optional[str],
        manager: LibraryManager
    ) -> Tuple[ScoreItem, MusicTimeline, Dict[str, Any]]:
        """
        Converts MML, writes to library directory, registers in DB, and generates MusicTimeline.
        All operations are transactional with safe tempdir isolation.
        """
        safe_title = self.sanitize_title(title)
        
        with tempfile.TemporaryDirectory() as temp_dir:
            temp_midi = os.path.join(temp_dir, f"{safe_title}.mid")
            
            # Step 1: Convert and verify MIDI
            stats = self.convert_to_midi_file(mml_text, temp_midi)
            
            # Step 2: Parse MusicTimeline using MidiImporter
            timeline = self.midi_importer.import_score(temp_midi)
            if timeline.total_notes == 0:
                raise ValueError("생성된 MIDI 파일에 유효한 음표 데이터가 없습니다.")
                
            timeline.title = safe_title
            
            # Step 3: Register into LibraryManager (copies to physical library dir and inserts into DB)
            score_item = manager.import_external_file(temp_midi, source_type="MML", folder_id=folder_id)
            
            # Step 4: Synchronize timeline metadata into DB
            manager.update_score_from_timeline(score_item.id, timeline)
            
            # Re-fetch updated item from DB
            updated_item = manager.db.get_score(score_item.id) or score_item
            
            return updated_item, timeline, stats
