"""
Roblox Piano Player - Pedal Backend
"""
import ctypes
from ctypes import wintypes
import time
from typing import Optional

# Win32 Constants
INPUT_MOUSE = 0
MOUSEEVENTF_MOVE = 0x0001
MOUSEEVENTF_ABSOLUTE = 0x8000
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004

class MousePedalBackend:
    def __init__(self):
        self._user32 = ctypes.windll.user32
        self.enabled = False
        self.x_ratio = 0.0
        self.y_ratio = 0.0
        self.interaction_mode = "toggle" # "toggle" or "hold"
        self.restore_cursor = True
        self._is_down = False

    def setup(self, enabled: bool, x_ratio: float, y_ratio: float, interaction_mode: str, restore_cursor: bool = True):
        self.enabled = enabled
        self.x_ratio = x_ratio
        self.y_ratio = y_ratio
        self.interaction_mode = interaction_mode
        self.restore_cursor = restore_cursor

    def _get_target_window_rect(self, hwnd) -> Optional[tuple]:
        rect = wintypes.RECT()
        if self._user32.GetClientRect(hwnd, ctypes.byref(rect)):
            # convert client rect to screen coordinates
            pt = wintypes.POINT(0, 0)
            self._user32.ClientToScreen(hwnd, ctypes.byref(pt))
            return (pt.x, pt.y, rect.right - rect.left, rect.bottom - rect.top)
        return None

    def _click(self, x: int, y: int, hwnd, hold: bool = False, release: bool = False):
        # Save cursor
        orig_pt = wintypes.POINT()
        self._user32.GetCursorPos(ctypes.byref(orig_pt))

        # Move to target
        self._user32.SetCursorPos(x, y)
        time.sleep(0.01)

        # Mouse event
        from src.playback.sendinput_backend import INPUT, MOUSEINPUT, _INPUTunion

        inp = INPUT()
        inp.type = INPUT_MOUSE
        inp.union.mi = MOUSEINPUT()
        inp.union.mi.dx = 0
        inp.union.mi.dy = 0
        inp.union.mi.mouseData = 0
        inp.union.mi.time = 0
        inp.union.mi.dwExtraInfo = 0

        flags = 0
        if not release:
            flags |= MOUSEEVENTF_LEFTDOWN
        if not hold:
            flags |= MOUSEEVENTF_LEFTUP

        inp.union.mi.dwFlags = flags
        self._user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))

        # Restore cursor
        if self.restore_cursor and not hold:
            time.sleep(0.01)
            self._user32.SetCursorPos(orig_pt.x, orig_pt.y)

    def pedal_down(self, target_hwnd):
        if not self.enabled or self._is_down:
            return
        
        rect = self._get_target_window_rect(target_hwnd)
        if not rect:
            return

        cx, cy, cw, ch = rect
        px = int(cx + self.x_ratio * cw)
        py = int(cy + self.y_ratio * ch)

        if self.interaction_mode == "toggle":
            self._click(px, py, target_hwnd, hold=False, release=False)
        else: # hold
            self._click(px, py, target_hwnd, hold=True, release=False)
        
        self._is_down = True

    def pedal_up(self, target_hwnd):
        if not self.enabled or not self._is_down:
            return

        rect = self._get_target_window_rect(target_hwnd)
        if not rect:
            return

        cx, cy, cw, ch = rect
        px = int(cx + self.x_ratio * cw)
        py = int(cy + self.y_ratio * ch)

        if self.interaction_mode == "toggle":
            self._click(px, py, target_hwnd, hold=False, release=False)
        else: # release hold
            self._click(px, py, target_hwnd, hold=False, release=True)

        self._is_down = False

    def release_all(self, target_hwnd):
        if self._is_down:
            self.pedal_up(target_hwnd)
