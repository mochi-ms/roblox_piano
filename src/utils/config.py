"""
Roblox Piano Player - Settings & Configuration Manager
"""
import os
import json
from dataclasses import dataclass, asdict, field
from typing import Dict, Any, Optional


@dataclass
class AppConfig:
    theme: str = "dark"
    countdown_seconds: int = 3
    playback_speed: float = 1.0
    transpose_semitones: int = 0
    enable_rh: bool = True
    enable_lh: bool = True
    hold_duration_ms: float = 30.0
    conflict_policy: str = "MICRO_ARPEGGIO"
    conflict_delay_ms: float = 8.0
    hotkey_play: str = "F6"
    hotkey_pause: str = "F7"
    hotkey_stop: str = "F8"
    hotkey_overlay: str = "F4"
    target_window_safety: bool = True
    focus_loss_policy: str = "PAUSE"
    overlay_opacity: float = 0.92
    overlay_compact: bool = False
    overlay_click_through: bool = False
    overlay_pos_x: int = 40
    overlay_pos_y: int = 40
    active_profile: str = "Roblox Virtual Piano 88"
    audiveris_path: str = ""
    dry_run_mode: bool = False
    last_directory: str = ""
    pedal_enabled: bool = False
    pedal_x_ratio: float = 0.5
    pedal_y_ratio: float = 0.5
    pedal_mode: str = "toggle"
    library_dir: str = ""


class ConfigManager:
    CONFIG_FILE = "settings.json"

    def __init__(self, config_dir: Optional[str] = None):
        self.config_dir = config_dir or os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
        self.config_path = os.path.join(self.config_dir, self.CONFIG_FILE)
        self.config: AppConfig = self.load_config()

    def load_config(self) -> AppConfig:
        cfg = AppConfig()
        if os.path.exists(self.config_path):
            try:
                with open(self.config_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                cfg = AppConfig(**{k: v for k, v in data.items() if k in AppConfig.__annotations__})
            except Exception:
                pass
        
        # Ensure library_dir is set to default LOCALAPPDATA path if empty
        if not cfg.library_dir:
            local_app_data = os.environ.get("LOCALAPPDATA", os.path.expanduser("~"))
            cfg.library_dir = os.path.join(local_app_data, "RobloxPianoPlayer", "Library")
            self.save_config(cfg)
            
        return cfg

    def save_config(self, config: Optional[AppConfig] = None) -> None:
        cfg = config or self.config
        try:
            with open(self.config_path, "w", encoding="utf-8") as f:
                json.dump(asdict(cfg), f, indent=2, ensure_ascii=False)
        except Exception as e:
            print(f"Error saving config: {e}")
