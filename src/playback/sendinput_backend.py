"""
Roblox Piano Player - Windows SendInput Hardware Scancode Backend
"""
import ctypes
from ctypes import wintypes
from typing import Dict, Set
from src.playback.keyboard_backend import KeyboardBackend


# Win32 Constants
INPUT_KEYBOARD = 1
KEYEVENTF_EXTENDEDKEY = 0x0001
KEYEVENTF_KEYUP = 0x0002
KEYEVENTF_UNICODE = 0x0004
KEYEVENTF_SCANCODE = 0x0008


# Ctypes struct definitions for SendInput
class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", wintypes.WORD),
        ("wScan", wintypes.WORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ctypes.c_ulonglong if ctypes.sizeof(ctypes.c_void_p) == 8 else ctypes.c_ulong),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wintypes.LONG),
        ("dy", wintypes.LONG),
        ("mouseData", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ctypes.c_ulonglong if ctypes.sizeof(ctypes.c_void_p) == 8 else ctypes.c_ulong),
    ]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [
        ("uMsg", wintypes.DWORD),
        ("wParamL", wintypes.WORD),
        ("wParamH", wintypes.WORD),
    ]


class _INPUTunion(ctypes.Union):
    _fields_ = [
        ("mi", MOUSEINPUT),
        ("ki", KEYBDINPUT),
        ("hi", HARDWAREINPUT),
    ]


class INPUT(ctypes.Structure):
    _fields_ = [
        ("type", wintypes.DWORD),
        ("union", _INPUTunion),
    ]


# Hardware DirectInput Scan Codes (QWERTY)
SCANCODE_MAP: Dict[str, int] = {
    # Number row
    "1": 0x02, "2": 0x03, "3": 0x04, "4": 0x05, "5": 0x06,
    "6": 0x07, "7": 0x08, "8": 0x09, "9": 0x0A, "0": 0x0B,
    # Top letter row
    "q": 0x10, "w": 0x11, "e": 0x12, "r": 0x13, "t": 0x14,
    "y": 0x15, "u": 0x16, "i": 0x17, "o": 0x18, "p": 0x19,
    # Home letter row
    "a": 0x1E, "s": 0x1F, "d": 0x20, "f": 0x21, "g": 0x22,
    "h": 0x23, "j": 0x24, "k": 0x25, "l": 0x26,
    # Bottom letter row
    "z": 0x2C, "x": 0x2D, "c": 0x2E, "v": 0x2F, "b": 0x30,
    "n": 0x31, "m": 0x32,
    # Modifiers & Controls
    "shift": 0x2A,  # Left Shift
    "lshift": 0x2A,
    "rshift": 0x36,
    "ctrl": 0x1D,
    "alt": 0x38,
    "space": 0x39,
    "enter": 0x1C,
    "esc": 0x01
}


class SendInputBackend(KeyboardBackend):
    """
    Simulates hardware keyboard input directly to Windows / DirectX via SendInput and KEYEVENTF_SCANCODE.
    """

    def __init__(self):
        self._pressed_scancodes: Set[int] = set()
        self._user32 = ctypes.windll.user32

    def _get_scancode(self, key_char: str) -> int:
        key_lower = key_char.lower()
        if key_lower in SCANCODE_MAP:
            return SCANCODE_MAP[key_lower]

        # Dynamic fallback: Map character to Virtual Key -> Scan Code
        try:
            vk = self._user32.VkKeyScanW(ord(key_char)) & 0xFF
            scan = self._user32.MapVirtualKeyW(vk, 0)
            if scan:
                return scan
        except Exception:
            pass

        return 0

    def _send_scancode(self, scancode: int, is_up: bool) -> None:
        if scancode <= 0:
            return

        flags = KEYEVENTF_SCANCODE
        if is_up:
            flags |= KEYEVENTF_KEYUP

        inp = INPUT()
        inp.type = INPUT_KEYBOARD
        inp.union.ki.wVk = 0
        inp.union.ki.wScan = scancode
        inp.union.ki.dwFlags = flags
        inp.union.ki.time = 0
        inp.union.ki.dwExtraInfo = 0

        self._user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))

    def key_down(self, key_char: str) -> None:
        scancode = self._get_scancode(key_char)
        if scancode > 0:
            self._send_scancode(scancode, is_up=False)
            self._pressed_scancodes.add(scancode)

    def key_up(self, key_char: str) -> None:
        scancode = self._get_scancode(key_char)
        if scancode > 0:
            self._send_scancode(scancode, is_up=True)
            self._pressed_scancodes.discard(scancode)

    def release_all(self) -> None:
        """Release any keys currently registered as pressed, plus explicit Left Shift release."""
        for scancode in list(self._pressed_scancodes):
            self._send_scancode(scancode, is_up=True)
        self._pressed_scancodes.clear()

        # Extra safety: unconditionally release Shift, Ctrl, Alt
        self._send_scancode(SCANCODE_MAP["shift"], is_up=True)
        self._send_scancode(SCANCODE_MAP["ctrl"], is_up=True)
        self._send_scancode(SCANCODE_MAP["alt"], is_up=True)
