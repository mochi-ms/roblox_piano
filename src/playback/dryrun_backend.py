"""
Roblox Piano Player - DryRun Backend (In-Memory Simulator for Safe Testing)
"""
import time
from typing import List, Tuple, Set
from src.playback.keyboard_backend import KeyboardBackend


class DryRunBackend(KeyboardBackend):
    """
    In-memory simulation backend for testing playback logic without sending real OS keystrokes.
    """

    def __init__(self):
        self.events: List[Tuple[float, str, str]] = []  # (timestamp, 'down'|'up', key_char)
        self.pressed_keys: Set[str] = set()

    def key_down(self, key_char: str) -> None:
        key_lower = key_char.lower()
        self.events.append((time.perf_counter(), "down", key_lower))
        self.pressed_keys.add(key_lower)

    def key_up(self, key_char: str) -> None:
        key_lower = key_char.lower()
        self.events.append((time.perf_counter(), "up", key_lower))
        self.pressed_keys.discard(key_lower)

    def release_all(self) -> None:
        for k in list(self.pressed_keys):
            self.events.append((time.perf_counter(), "up", k))
        self.pressed_keys.clear()

    def clear(self) -> None:
        self.events.clear()
        self.pressed_keys.clear()
