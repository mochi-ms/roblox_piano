"""
Roblox Piano Player - Piano Mapper
"""
from typing import Optional, Dict
from src.music.events import NoteEvent
from src.piano.profile import PianoProfile, KeyMapping, ProfileManager


class RobloxPianoMapper:
    """
    Translates MIDI note pitches to physical keys and Shift states based on a PianoProfile.
    """

    def __init__(self, profile: Optional[PianoProfile] = None):
        self.profile: PianoProfile = profile or ProfileManager.load_default_profile()
        self._char_to_key_map: Dict[str, KeyMapping] = {}
        self._rebuild_cache()

    def set_profile(self, profile: PianoProfile) -> None:
        self.profile = profile
        self._rebuild_cache()

    def _rebuild_cache(self) -> None:
        self._char_to_key_map.clear()
        for km in self.profile.keys.values():
            self._char_to_key_map[km.char] = km

    def map_pitch(self, pitch: int) -> Optional[KeyMapping]:
        return self.profile.keys.get(pitch)

    def map_note_event(self, note: NoteEvent) -> Optional[KeyMapping]:
        return self.map_pitch(note.pitch)

    def can_play(self, pitch: int) -> bool:
        return pitch in self.profile.keys

    def get_by_char(self, char: str) -> Optional[KeyMapping]:
        return self._char_to_key_map.get(char)

    @property
    def min_pitch(self) -> int:
        return self.profile.min_pitch

    @property
    def max_pitch(self) -> int:
        return self.profile.max_pitch
