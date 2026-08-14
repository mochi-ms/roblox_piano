"""
Roblox Piano Player - Music Event Definitions
"""
from dataclasses import dataclass, field
from enum import Enum
from typing import Optional, List, Dict, Any


class HandType(Enum):
    RIGHT = "RH"
    LEFT = "LH"
    BOTH = "BOTH"
    AUTO = "AUTO"
    UNKNOWN = "UNKNOWN"

    @classmethod
    def from_string(cls, val: str) -> "HandType":
        val_upper = val.upper().strip()
        if val_upper in ("RH", "RIGHT", "UPPER", "TREBLE"):
            return cls.RIGHT
        if val_upper in ("LH", "LEFT", "LOWER", "BASS"):
            return cls.LEFT
        if val_upper in ("BOTH", "ALL"):
            return cls.BOTH
        return cls.UNKNOWN


@dataclass
class NoteEvent:
    pitch: int                     # MIDI note number (e.g. C4 = 60, C2 = 36, C7 = 96)
    start_time: float             # In seconds (absolute time from song start)
    end_time: float               # In seconds
    velocity: int = 64            # 0-127
    hand: HandType = HandType.AUTO
    staff: Optional[int] = None   # Staff index (e.g. 1=upper, 2=lower in MusicXML)
    track: Optional[int] = None   # MIDI track index
    channel: Optional[int] = 0    # MIDI channel (0-15)
    source: str = "default"       # "midi", "musicxml", "numeric", "omr"
    original_pitch: Optional[int] = None  # Before transpose or octave-fit

    @property
    def duration(self) -> float:
        return max(0.01, self.end_time - self.start_time)

    def is_in_range(self, min_pitch: int = 36, max_pitch: int = 96) -> bool:
        return min_pitch <= self.pitch <= max_pitch


@dataclass
class ChordGroup:
    """Group of notes starting at virtually the same timestamp"""
    start_time: float
    notes: List[NoteEvent] = field(default_factory=list)

    @property
    def max_end_time(self) -> float:
        if not self.notes:
            return self.start_time
        return max(n.end_time for n in self.notes)
