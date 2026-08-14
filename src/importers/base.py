"""
Roblox Piano Player - Base Importer Interface
"""
from abc import ABC, abstractmethod
from typing import List
from src.music.timeline import MusicTimeline


class BaseMusicImporter(ABC):
    """
    Abstract interface for importing different music formats into a normalized MusicTimeline.
    """

    @abstractmethod
    def can_import(self, file_path_or_content: str) -> bool:
        """Returns True if this importer supports the given file or string format."""
        pass

    @abstractmethod
    def import_score(self, file_path_or_content: str, **kwargs) -> MusicTimeline:
        """Parses the score and returns a normalized MusicTimeline."""
        pass

    @property
    @abstractmethod
    def supported_extensions(self) -> List[str]:
        pass
