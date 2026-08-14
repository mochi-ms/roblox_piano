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
        self._active_modifiers: Set[str] = set()
        self._lock = threading.Lock()

    @property
    def active_modifiers(self) -> Set[str]:
        with self._lock:
            return set(self._active_modifiers)

    @property
    def active_keys(self) -> Set[str]:
        with self._lock:
            return set(self._pressed_physical_keys)

    def set_modifier(self, modifier: str, active: bool) -> None:
        mod_upper = modifier.upper()
        with self._lock:
            if active and mod_upper not in self._active_modifiers:
                self.backend.key_down(mod_upper.lower())
                self._active_modifiers.add(mod_upper)
            elif not active and mod_upper in self._active_modifiers:
                self.backend.key_up(mod_upper.lower())
                self._active_modifiers.discard(mod_upper)

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
        Guarantees that all modifiers and physical keys are completely released.
        """
        with self._lock:
            for k in list(self._pressed_physical_keys):
                try:
                    self.backend.key_up(k)
                except Exception:
                    pass
            self._pressed_physical_keys.clear()

            for mod in list(self._active_modifiers):
                try:
                    self.backend.key_up(mod.lower())
                except Exception:
                    pass
            self._active_modifiers.clear()

            # Call backend's native release_all
            try:
                self.backend.release_all()
            except Exception:
                pass
