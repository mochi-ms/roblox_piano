"""
Roblox Piano Player - Key State Manager
"""
import threading
from typing import Set, Tuple
from src.playback.keyboard_backend import KeyboardBackend


class KeyStateManager:
    """
    Tracks and manages currently active physical keys and Shift state.
    Ensures safe, atomic key release and prevents key stuck states.
    """

    def __init__(self, backend: KeyboardBackend):
        self.backend: KeyboardBackend = backend
        self._pressed_physical_keys: Set[str] = set()
        self._shift_active: bool = False
        self._lock = threading.Lock()

    @property
    def shift_active(self) -> bool:
        return self._shift_active

    @property
    def active_keys(self) -> Set[str]:
        with self._lock:
            return set(self._pressed_physical_keys)

    def set_shift(self, active: bool) -> None:
        with self._lock:
            if active and not self._shift_active:
                self.backend.key_down("shift")
                self._shift_active = True
            elif not active and self._shift_active:
                self.backend.key_up("shift")
                self._shift_active = False

    def press_physical_key(self, physical_key: str) -> None:
        with self._lock:
            key_lower = physical_key.lower()
            self.backend.key_down(key_lower)
            self._pressed_physical_keys.add(key_lower)

    def release_physical_key(self, physical_key: str) -> None:
        with self._lock:
            key_lower = physical_key.lower()
            if key_lower in self._pressed_physical_keys:
                self.backend.key_up(key_lower)
                self._pressed_physical_keys.discard(key_lower)

    def release_all(self) -> None:
        """
        Emergency or routine full release.
        Guarantees that Shift and all physical keys are completely released.
        """
        with self._lock:
            for k in list(self._pressed_physical_keys):
                try:
                    self.backend.key_up(k)
                except Exception:
                    pass
            self._pressed_physical_keys.clear()

            if self._shift_active:
                try:
                    self.backend.key_up("shift")
                except Exception:
                    pass
                self._shift_active = False

            # Call backend's native release_all
            try:
                self.backend.release_all()
            except Exception:
                pass
