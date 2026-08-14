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
        Executes a chord of NoteEvents with proper Shift isolation and conflict resolution.
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

        # 2. Check for same physical key conflicts (e.g. 'q' unshifted vs 'q' shifted)
        physical_key_map: Dict[str, List[KeyMapping]] = {}
        for _, km in mapped_keys:
            pk = km.physical_key.lower()
            physical_key_map.setdefault(pk, []).append(km)

        conflicted_keys = {pk: kms for pk, kms in physical_key_map.items() if len(kms) > 1}

        # If conflict exists
        if conflicted_keys:
            for pk, kms in conflicted_keys.items():
                chars = [k.char for k in kms]
                self._log(f"Physical key conflict on '{pk}': {chars} -> applying {self.conflict_policy.value}")

            if self.conflict_policy == ConflictPolicy.MICRO_ARPEGGIO:
                # Play unshifted first, then shifted with micro delay
                unshifted = [km for _, km in mapped_keys if not km.shift]
                shifted = [km for _, km in mapped_keys if km.shift]

                # Step 1: Play unshifted
                if unshifted:
                    for km in unshifted:
                        self.key_state.press_physical_key(km.physical_key)
                    time.sleep(delay_sec)
                    for km in unshifted:
                        self.key_state.release_physical_key(km.physical_key)

                # Step 2: Play shifted
                if shifted:
                    self.key_state.set_shift(True)
                    time.sleep(0.002)
                    for km in shifted:
                        self.key_state.press_physical_key(km.physical_key)
                    time.sleep(hold_sec)
                    for km in shifted:
                        self.key_state.release_physical_key(km.physical_key)
                    self.key_state.set_shift(False)
                return

            elif self.conflict_policy == ConflictPolicy.SKIP_CONFLICTED:
                # Keep only the highest pitch note for each physical key
                filtered_mapped = []
                seen_pk = set()
                # Sort by pitch descending
                for n, km in sorted(mapped_keys, key=lambda item: item[0].pitch, reverse=True):
                    pk = km.physical_key.lower()
                    if pk not in seen_pk:
                        seen_pk.add(pk)
                        filtered_mapped.append((n, km))
                mapped_keys = filtered_mapped

        # 3. Standard Mixed Chord Execution (No direct key conflict)
        unshifted_keys = [km for _, km in mapped_keys if not km.shift]
        shifted_keys = [km for _, km in mapped_keys if km.shift]

        # Order of operations for clean sound without modifier bleeding:
        # 1. Press unshifted keys
        # 2. If shifted keys exist: set Shift, press shifted keys
        # 3. Hold duration
        # 4. Release shifted keys, release Shift
        # 5. Release unshifted keys

        for km in unshifted_keys:
            self.key_state.press_physical_key(km.physical_key)

        if shifted_keys:
            self.key_state.set_shift(True)
            time.sleep(0.002)
            for km in shifted_keys:
                self.key_state.press_physical_key(km.physical_key)

        # Hold for duration
        time.sleep(hold_sec)

        if shifted_keys:
            for km in shifted_keys:
                self.key_state.release_physical_key(km.physical_key)
            time.sleep(0.002)
            self.key_state.set_shift(False)

        for km in unshifted_keys:
            self.key_state.release_physical_key(km.physical_key)
