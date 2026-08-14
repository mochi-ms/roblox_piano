"""
Roblox Piano Player - Roblox Target Window Detection & Safety
"""
import ctypes
from ctypes import wintypes
from enum import Enum
from typing import Tuple, Optional


class FocusLossPolicy(Enum):
    PAUSE = "PAUSE"
    STOP = "STOP"
    CONTINUE = "CONTINUE"


class TargetWindowManager:
    """
    Monitors active foreground windows to detect Roblox and prevent keystrokes
    from leaking into browsers, chat apps, or text editors.
    """

    ROBLOX_KEYWORDS = ("roblox", "robloxplayer", "robloxplayerbeta")

    def __init__(self, policy: FocusLossPolicy = FocusLossPolicy.PAUSE, enabled: bool = True):
        self.policy: FocusLossPolicy = policy
        self.enabled: bool = enabled
        self._user32 = ctypes.windll.user32

    def get_foreground_window_title(self) -> str:
        hwnd = self._user32.GetForegroundWindow()
        if not hwnd:
            return ""
        length = self._user32.GetWindowTextLengthW(hwnd)
        if length == 0:
            return ""
        buff = ctypes.create_unicode_buffer(length + 1)
        self._user32.GetWindowTextW(hwnd, buff, length + 1)
        return buff.value

    def is_roblox_foreground(self) -> bool:
        if not self.enabled:
            return True  # If safety check is disabled by user, always allow

        title = self.get_foreground_window_title().lower()
        if not title:
            return False

        # Match "Roblox" window title
        return any(k in title for k in self.ROBLOX_KEYWORDS)

    def check_can_play(self) -> Tuple[bool, str]:
        """
        Returns (can_play, reason_or_warning)
        """
        if not self.enabled:
            return True, "Target window check disabled"

        if self.is_roblox_foreground():
            return True, "Roblox is foreground window"

        current_title = self.get_foreground_window_title() or "None"
        return False, f"로블록스 창이 활성화되어 있지 않습니다 (현재: '{current_title}')"
