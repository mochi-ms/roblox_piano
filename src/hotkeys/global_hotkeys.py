"""
Roblox Piano Player - Global Hotkeys Manager
"""
import keyboard
from typing import Callable, Dict, Optional


class GlobalHotkeyManager:
    """
    Manages global keyboard shortcuts (e.g. F6=Play, F7=Pause/Resume, F8=Stop, F4=Toggle Overlay).
    """

    def __init__(self):
        self.play_hotkey: str = "f6"
        self.pause_hotkey: str = "f7"
        self.stop_hotkey: str = "f8"
        self.overlay_hotkey: str = "f4"

        self._registered_hooks: Dict[str, any] = {}
        self._is_active: bool = False

    def register(
        self,
        on_play: Optional[Callable[[], None]] = None,
        on_pause: Optional[Callable[[], None]] = None,
        on_stop: Optional[Callable[[], None]] = None,
        on_toggle_overlay: Optional[Callable[[], None]] = None
    ) -> None:
        self.unregister_all()

        try:
            if on_play:
                hook = keyboard.add_hotkey(self.play_hotkey, on_play, suppress=False)
                self._registered_hooks[self.play_hotkey] = hook
            if on_pause:
                hook = keyboard.add_hotkey(self.pause_hotkey, on_pause, suppress=False)
                self._registered_hooks[self.pause_hotkey] = hook
            if on_stop:
                hook = keyboard.add_hotkey(self.stop_hotkey, on_stop, suppress=False)
                self._registered_hooks[self.stop_hotkey] = hook
            if on_toggle_overlay:
                hook = keyboard.add_hotkey(self.overlay_hotkey, on_toggle_overlay, suppress=False)
                self._registered_hooks[self.overlay_hotkey] = hook

            self._is_active = True
        except Exception as e:
            print(f"Warning: Global hotkey registration failed: {e}")

    def unregister_all(self) -> None:
        try:
            keyboard.unhook_all_hotkeys()
        except Exception:
            pass
        self._registered_hooks.clear()
        self._is_active = False

    def update_hotkeys(
        self,
        play_hk: Optional[str] = None,
        pause_hk: Optional[str] = None,
        stop_hk: Optional[str] = None,
        overlay_hk: Optional[str] = None
    ) -> None:
        if play_hk:
            self.play_hotkey = play_hk.lower()
        if pause_hk:
            self.pause_hotkey = pause_hk.lower()
        if stop_hk:
            self.stop_hotkey = stop_hk.lower()
        if overlay_hk:
            self.overlay_hotkey = overlay_hk.lower()
