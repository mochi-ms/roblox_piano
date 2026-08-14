"""
Roblox Piano Player - Chord & Mixed Key Playback Engine
"""
import time
from enum import Enum
from typing import List, Dict, Tuple, Optional, Callable
from src.music.events import NoteEvent
from src.piano.mapper import RobloxPianoMapper
from src.piano.profile import KeyMapping
from src.playback.key_state_manager import KeyStateManager


class ConflictPolicy(Enum):
    MICRO_ARPEGGIO = "MICRO_ARPEGGIO"
    SKIP_CONFLICTED = "SKIP_CONFLICTED"
    WARN_ONLY = "WARN_ONLY"


class ChordEngine:
    """
    Handles mixed Shift/Unshifted chords and resolves same physical key conflicts
    (e.g., q + Q sharing the same 'Q' physical key).
    """

    def __init__(
        self,
        key_state: KeyStateManager,
        mapper: RobloxPianoMapper,
        conflict_policy: ConflictPolicy = ConflictPolicy.MICRO_ARPEGGIO,
        conflict_delay_ms: float = 8.0,
        default_hold_duration_ms: float = 30.0,
        on_log: Optional[Callable[[str], None]] = None
    ):
        self.key_state: KeyStateManager = key_state
        self.mapper: RobloxPianoMapper = mapper
        self.conflict_policy: ConflictPolicy = conflict_policy
        self.conflict_delay_ms: float = conflict_delay_ms
        self.default_hold_duration_ms: float = default_hold_duration_ms
        self.on_log: Optional[Callable[[str], None]] = on_log

    def _log(self, message: str) -> None:
        if self.on_log:
            self.on_log(message)

    def play_chord_notes(
        self,
        notes: List[NoteEvent],
        hold_duration_ms: Optional[float] = None
    ) -> None:
        """
        Executes a chord of NoteEvents with proper modifier isolation and conflict resolution.
        """
        if not notes:
            return

        hold_ms = hold_duration_ms or self.default_hold_duration_ms
        hold_sec = max(0.01, hold_ms / 1000.0)
        delay_sec = self.conflict_delay_ms / 1000.0

        # 1. Map notes to physical keys
        mapped_keys: List[Tuple[NoteEvent, KeyMapping]] = []
        for n in notes:
            km = self.mapper.map_pitch(n.pitch)
            if km:
                mapped_keys.append((n, km))

        if not mapped_keys:
            return

        # 2. Check for same physical key conflicts
        physical_key_map: Dict[str, List[KeyMapping]] = {}
        for _, km in mapped_keys:
            pk = km.physical_key.lower()
            physical_key_map.setdefault(pk, []).append(km)

        conflicted_keys = {pk: kms for pk, kms in physical_key_map.items() if len(kms) > 1}

        if conflicted_keys and self.conflict_policy == ConflictPolicy.SKIP_CONFLICTED:
            # Keep only the highest pitch note for each physical key
            filtered_mapped = []
            seen_pk = set()
            for n, km in sorted(mapped_keys, key=lambda item: item[0].pitch, reverse=True):
                pk = km.physical_key.lower()
                if pk not in seen_pk:
                    seen_pk.add(pk)
                    filtered_mapped.append((n, km))
            mapped_keys = filtered_mapped

        # 3. Group by modifier sets (Execution Plan)
        # We must serialize different modifier groups to prevent modifier bleeding.
        # e.g., if we press Shift for black keys, we don't want it to affect Normal keys.
        modifier_groups: Dict[frozenset, List[KeyMapping]] = {}
        for _, km in mapped_keys:
            modifier_groups.setdefault(km.modifiers, []).append(km)

        # Sort groups: empty modifiers first, then others, to maintain a consistent execution order
        sorted_mod_groups = sorted(modifier_groups.items(), key=lambda x: (len(x[0]), list(x[0])))

        is_multi_group = len(sorted_mod_groups) > 1 or conflicted_keys

        if is_multi_group:
            # Micro-Arpeggio execution plan
            for i, (mods, kms) in enumerate(sorted_mod_groups):
                # Set modifiers for this group
                for mod in mods:
                    self.key_state.set_modifier(mod, True)
                if mods:
                    time.sleep(0.002)

                # Press keys
                for km in kms:
                    self.key_state.press_physical_key(km.physical_key)
                
                # Hold for micro duration if not the last group, else full duration
                if i < len(sorted_mod_groups) - 1:
                    time.sleep(delay_sec)
                else:
                    time.sleep(hold_sec)

                # Release keys
                for km in kms:
                    self.key_state.release_physical_key(km.physical_key)
                
                # Release modifiers
                for mod in mods:
                    self.key_state.set_modifier(mod, False)
                if mods:
                    time.sleep(0.002)
        else:
            # Single modifier group, standard execution
            mods, kms = sorted_mod_groups[0]
            
            for mod in mods:
                self.key_state.set_modifier(mod, True)
            if mods:
                time.sleep(0.002)

            for km in kms:
                self.key_state.press_physical_key(km.physical_key)

            time.sleep(hold_sec)

            for km in kms:
                self.key_state.release_physical_key(km.physical_key)

            for mod in mods:
                self.key_state.set_modifier(mod, False)
