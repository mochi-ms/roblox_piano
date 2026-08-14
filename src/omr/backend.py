"""
Roblox Piano Player - OMR Backend Interface
"""
from abc import ABC, abstractmethod
from dataclasses import dataclass
from enum import Enum
from typing import Optional


class OMRStatus(Enum):
    SUCCESS = "SUCCESS"
    NOT_INSTALLED = "NOT_INSTALLED"
    PROCESSING_ERROR = "PROCESSING_ERROR"
    INVALID_INPUT = "INVALID_INPUT"


@dataclass
class OMRResult:
    status: OMRStatus
    musicxml_path: Optional[str] = None
    error_message: Optional[str] = None
    raw_output: Optional[str] = None


class BaseOMRBackend(ABC):
    """
    Abstract Optical Music Recognition backend.
    """

    @abstractmethod
    def is_available(self) -> bool:
        """Returns True if the underlying OMR engine (e.g. Audiveris) is installed and accessible."""
        pass

    @abstractmethod
    def recognize_score(self, image_path: str, output_dir: Optional[str] = None) -> OMRResult:
        """Runs OMR on a score image and outputs a MusicXML file."""
        pass
