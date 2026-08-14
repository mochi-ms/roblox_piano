import time
from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class ScoreItem:
    """
    Represents a single score entry in the local library.
    """
    id: str  # Unique UUID or hash
    title: str
    source_type: str  # "FILE", "YOUTUBE", "IMAGE_OMR"
    source_url: str   # File path or YouTube URL used as source
    filepath: str     # Path to the actual .xml or .mid file inside Library dir
    duration: float = 0.0
    bpm: float = 120.0
    total_notes: int = 0
    tags: str = ""    # Comma-separated tags e.g. "anime,hard,omr"
    created_at: float = field(default_factory=time.time)

    def get_tags_list(self) -> List[str]:
        if not self.tags:
            return []
        return [t.strip() for t in self.tags.split(",") if t.strip()]

    def set_tags_list(self, tags: List[str]) -> None:
        self.tags = ",".join(tags)
