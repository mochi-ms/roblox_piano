"""
Roblox Piano Player - In-App Event & Diagnostics Logger
"""
import time
from typing import List, Callable, Optional


class AppLogger:
    def __init__(self, max_entries: int = 1000):
        self.max_entries: int = max_entries
        self.logs: List[str] = []
        self._listeners: List[Callable[[str], None]] = []

    def add_listener(self, listener: Callable[[str], None]) -> None:
        self._listeners.append(listener)

    def remove_listener(self, listener: Callable[[str], None]) -> None:
        if listener in self._listeners:
            self._listeners.remove(listener)

    def log(self, message: str) -> None:
        timestamp = time.strftime("%H:%M:%S")
        formatted = f"[{timestamp}] {message}"
        self.logs.append(formatted)
        if len(self.logs) > self.max_entries:
            self.logs.pop(0)

        for l in self._listeners:
            try:
                l(formatted)
            except Exception:
                pass


# Global logger instance
logger = AppLogger()
