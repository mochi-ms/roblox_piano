"""
Roblox Piano Player - Normalized Music Timeline
"""
from typing import List, Optional, Dict, Tuple
from src.music.events import NoteEvent, ChordGroup, HandType, PedalEvent


class MusicTimeline:
    """
    Unified absolute-time timeline for all music events.
    Independent of the original source format (MIDI, MusicXML, etc.)
    """

    def __init__(self, title: str = "Untitled"):
        self.title: str = title
        self.notes: List[NoteEvent] = []
        self.pedals: List[PedalEvent] = []
        self.initial_bpm: float = 120.0
        self.time_signature: Tuple[int, int] = (4, 4)
        self.track_names: Dict[int, str] = {}
        self.metadata: Dict[str, any] = {}

    def add_note(self, note: NoteEvent) -> None:
        self.notes.append(note)

    def add_pedal(self, pedal: PedalEvent) -> None:
        self.pedals.append(pedal)

    def sort_events(self) -> None:
        """Sort notes and pedals by start_time ascending."""
        self.notes.sort(key=lambda n: (n.start_time, n.pitch))
        self.pedals.sort(key=lambda p: p.time)

    @property
    def total_notes(self) -> int:
        return len(self.notes)

    @property
    def duration(self) -> float:
        note_dur = max((n.end_time for n in self.notes), default=0.0)
        pedal_dur = max((p.time for p in self.pedals), default=0.0)
        return max(note_dur, pedal_dur)

    @property
    def pitch_range(self) -> Tuple[int, int]:
        if not self.notes:
            return (60, 60)
        pitches = [n.pitch for n in self.notes]
        return (min(pitches), max(pitches))

    def get_hand_note_counts(self) -> Tuple[int, int, int]:
        """Returns (RH count, LH count, Other/Unknown count)"""
        rh = sum(1 for n in self.notes if n.hand == HandType.RIGHT)
        lh = sum(1 for n in self.notes if n.hand == HandType.LEFT)
        other = len(self.notes) - rh - lh
        return (rh, lh, other)

    def get_out_of_range_notes(self, min_pitch: int = 36, max_pitch: int = 96) -> List[NoteEvent]:
        """Roblox 61-key piano range is C2 (36) to C7 (96)"""
        return [n for n in self.notes if not (min_pitch <= n.pitch <= max_pitch)]

    def get_filtered_notes(
        self,
        enable_rh: bool = True,
        enable_lh: bool = True,
        track_filter: Optional[Dict[int, bool]] = None
    ) -> List[NoteEvent]:
        """Filter notes based on active hands and tracks."""
        filtered = []
        for n in self.notes:
            # Track filter
            if track_filter is not None and n.track is not None:
                if not track_filter.get(n.track, True):
                    continue

            # Hand filter
            if n.hand == HandType.RIGHT and not enable_rh:
                continue
            elif n.hand == HandType.LEFT and not enable_lh:
                continue
            elif n.hand not in (HandType.RIGHT, HandType.LEFT):
                # If neither RH nor LH explicitly, allow if at least one is enabled
                if not enable_rh and not enable_lh:
                    continue

            filtered.append(n)
        return filtered

    def build_chord_groups(
        self,
        notes: Optional[List[NoteEvent]] = None,
        tolerance: float = 0.015  # 15ms tolerance for grouping simultaneous notes
    ) -> List[ChordGroup]:
        """
        Group notes that start at virtually the same timestamp into ChordGroups.
        """
        target_notes = notes if notes is not None else self.notes
        if not target_notes:
            return []

        sorted_notes = sorted(target_notes, key=lambda n: (n.start_time, n.pitch))
        chord_groups: List[ChordGroup] = []

        current_group: Optional[ChordGroup] = None

        for note in sorted_notes:
            if current_group is None:
                current_group = ChordGroup(start_time=note.start_time, notes=[note])
            elif abs(note.start_time - current_group.start_time) <= tolerance:
                current_group.notes.append(note)
            else:
                chord_groups.append(current_group)
                current_group = ChordGroup(start_time=note.start_time, notes=[note])

        if current_group is not None:
            chord_groups.append(current_group)

        return chord_groups
