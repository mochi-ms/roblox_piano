"""
Roblox Piano Player - PDF Score Importer
"""
import os
import tempfile
from typing import List, Optional
from src.importers.base import BaseMusicImporter
from src.importers.image_importer import ImageImporter
from src.music.timeline import MusicTimeline
from src.omr.backend import BaseOMRBackend
from src.omr.audiveris_backend import AudiverisBackend


class PdfImporter(BaseMusicImporter):
    """
    Imports PDF score files by rendering pages and processing via OMR.
    """

    def __init__(self, omr_backend: Optional[BaseOMRBackend] = None):
        self.omr_backend: BaseOMRBackend = omr_backend or AudiverisBackend()
        self.image_importer = ImageImporter(omr_backend=self.omr_backend)

    @property
    def supported_extensions(self) -> List[str]:
        return [".pdf"]

    def can_import(self, file_path_or_content: str) -> bool:
        if not os.path.isfile(file_path_or_content):
            return False
        ext = os.path.splitext(file_path_or_content)[1].lower()
        return ext in self.supported_extensions

    def import_score(self, file_path_or_content: str, **kwargs) -> MusicTimeline:
        if not os.path.isfile(file_path_or_content):
            raise FileNotFoundError(f"PDF file not found: {file_path_or_content}")

        if not self.omr_backend.is_available():
            raise RuntimeError(
                f"OMR Engine (Audiveris) is not installed.\n\n"
                f"To recognize PDF sheet music, please install Audiveris.\n"
                f"Directly supported formats without OMR: MIDI (.mid), MusicXML (.musicxml, .xml, .mxl), Numeric (.txt)"
            )

        # Audiveris can directly accept PDF files!
        result = self.omr_backend.recognize_score(file_path_or_content)
        if result.status != result.status.SUCCESS or not result.musicxml_path:
            raise RuntimeError(f"PDF OMR Recognition failed: {result.error_message}")

        timeline = self.image_importer.musicxml_importer.import_score(result.musicxml_path)
        timeline.title = os.path.splitext(os.path.basename(file_path_or_content))[0]
        return timeline
