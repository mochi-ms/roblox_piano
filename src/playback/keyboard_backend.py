"""
Roblox Piano Player - Keyboard Backend Abstract Interface
"""
from abc import ABC, abstractmethod
from typing import Set


class KeyboardBackend(ABC):
    """
    Abstract interface for keyboard input simulation.
    """

    @abstractmethod
    def key_down(self, key_char: str) -> None:
        """Press down a physical key (e.g. '1', 'q', 't', 'shift')"""
        pass

    @abstractmethod
    def key_up(self, key_char: str) -> None:
        """Release a physical key"""
        pass

    @abstractmethod
    def release_all(self) -> None:
        """Release all currently pressed keys immediately"""
        pass
