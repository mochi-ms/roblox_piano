"""
Roblox Piano Player - Hand Assignment Module
"""
from typing import List, Dict, Optional
from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline


class HandAssigner:
    """
    Infers or assigns Left Hand / Right Hand to note events.
    Rules:
    1. If Staff is explicitly set (e.g. MusicXML grand staff): Staff 1 -> RH, Staff 2 -> LH.
    2. If Track names contain 'right', 'left', 'rh', 'lh', 'treble', 'bass', 'upper', 'lower'.
    3. User overrides per track/staff.
    4. Fallback pitch-based heuristic: split at MIDI pitch split_point (default C4 = 60).
    """

    @staticmethod
    def assign_hands_to_timeline(
        timeline: MusicTimeline,
        track_hand_overrides: Optional[Dict[int, HandType]] = None,
        split_point: int = 60
    ) -> None:
        overrides = track_hand_overrides or {}

        # First pass: check track names if no explicit override
        track_inferred: Dict[int, HandType] = {}
        for track_idx, name in timeline.track_names.items():
            if track_idx in overrides:
                track_inferred[track_idx] = overrides[track_idx]
                continue
            name_lower = name.lower()
            if any(k in name_lower for k in ("right", "rh", "treble", "upper", "soprano", "melody")):
                track_inferred[track_idx] = HandType.RIGHT
            elif any(k in name_lower for k in ("left", "lh", "bass", "lower", "accomp")):
                track_inferred[track_idx] = HandType.LEFT
            else:
                track_inferred[track_idx] = HandType.AUTO

        for note in timeline.notes:
            # 1. Check user override for track
            if note.track is not None and note.track in overrides:
                note.hand = overrides[note.track]
                continue

            # 2. Check track inferred hand
            if note.track is not None and note.track in track_inferred and track_inferred[note.track] != HandType.AUTO:
                note.hand = track_inferred[note.track]
                continue

            # 3. Check staff (MusicXML)
            if note.staff == 1:
                note.hand = HandType.RIGHT
                continue
            elif note.staff == 2:
                note.hand = HandType.LEFT
                continue

            # 4. Fallback pitch split
            if note.pitch >= split_point:
                note.hand = HandType.RIGHT
            else:
                note.hand = HandType.LEFT
