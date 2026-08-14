"""
Roblox Piano Player - Profile Definition and Loader
"""
import json
import os
from dataclasses import dataclass, field
from typing import Dict, Optional, List


@dataclass
class KeyMapping:
    pitch: int
    char: str
    physical_key: str
    modifiers: frozenset[str]
    name: str  # e.g. "C4", "C#4"



@dataclass
class PianoProfile:
    name: str
    description: str
    version: str
    min_pitch: int
    max_pitch: int
    keys: Dict[int, KeyMapping] = field(default_factory=dict)
    sustain_pedal: Optional[str] = None
    file_path: Optional[str] = None

    @classmethod
    def from_dict(cls, data: dict, file_path: Optional[str] = None) -> "PianoProfile":
        keys_dict = {}
        for pitch_str, k_data in data.get("keys", {}).items():
            pitch = int(pitch_str)
            # Migrate legacy 'shift' boolean if present
            modifiers_list = k_data.get("modifiers", [])
            if k_data.get("shift", False) and "SHIFT" not in modifiers_list:
                modifiers_list.append("SHIFT")
            
            keys_dict[pitch] = KeyMapping(
                pitch=pitch,
                char=k_data["char"],
                physical_key=k_data["physical_key"],
                modifiers=frozenset(modifiers_list),
                name=k_data.get("name", "")
            )

        return cls(
            name=data.get("name", "Unknown Profile"),
            description=data.get("description", ""),
            version=data.get("version", "1.0"),
            min_pitch=data.get("min_pitch", 36),
            max_pitch=data.get("max_pitch", 96),
            keys=keys_dict,
            sustain_pedal=data.get("sustain_pedal"),
            file_path=file_path
        )

    def to_dict(self) -> dict:
        keys_dict = {}
        for pitch, k in self.keys.items():
            keys_dict[str(pitch)] = {
                "char": k.char,
                "physical_key": k.physical_key,
                "modifiers": list(k.modifiers),
                "name": k.name
            }
        return {
            "name": self.name,
            "description": self.description,
            "version": self.version,
            "min_pitch": self.min_pitch,
            "max_pitch": self.max_pitch,
            "sustain_pedal": self.sustain_pedal,
            "keys": keys_dict
        }

    def save(self, file_path: Optional[str] = None) -> None:
        path = file_path or self.file_path
        if not path:
            raise ValueError("No file path specified for saving profile.")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, indent=2, ensure_ascii=False)


class ProfileManager:
    @staticmethod
    def get_profiles_dir() -> str:
        current_dir = os.path.dirname(os.path.abspath(__file__))
        return os.path.join(current_dir, "profiles")

    @classmethod
    def get_default_profile_path(cls) -> str:
        return os.path.join(cls.get_profiles_dir(), "roblox_virtual_piano_88.json")

    @classmethod
    def load_default_profile(cls) -> PianoProfile:
        path = cls.get_default_profile_path()
        return cls.load_profile(path)

    @classmethod
    def load_profile(cls, file_path: str) -> PianoProfile:
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        return PianoProfile.from_dict(data, file_path=file_path)

    @classmethod
    def list_available_profiles(cls) -> List[PianoProfile]:
        profiles = []
        profiles_dir = cls.get_profiles_dir()
        if os.path.exists(profiles_dir):
            for fname in os.listdir(profiles_dir):
                if fname.endswith(".json"):
                    try:
                        p = cls.load_profile(os.path.join(profiles_dir, fname))
                        profiles.append(p)
                    except Exception:
                        pass
        return profiles
