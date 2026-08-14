"""
Unit tests for 61-key Roblox Virtual Piano Profile & Mapper
"""
import pytest
from src.piano.mapper import RobloxPianoMapper
from src.piano.profile import ProfileManager


def test_default_profile_loading():
    profile = ProfileManager.load_default_profile()
    assert profile.name == "Roblox Virtual Piano 88-Key"
    assert profile.min_pitch == 21
    assert profile.max_pitch == 108
    assert len(profile.keys) == 88


def test_key_mappings_c2_to_c7():
    mapper = RobloxPianoMapper()

    # C2 (36) -> '1' (No Shift)
    c2 = mapper.map_pitch(36)
    assert c2 is not None
    assert c2.char == "1"
    assert c2.physical_key == "1"
    assert "SHIFT" not in c2.modifiers

    # C#2 (37) -> 'Shift+1'
    cs2 = mapper.map_pitch(37)
    assert cs2 is not None
    assert cs2.char == "Shift+1"
    assert cs2.physical_key == "1"
    assert "SHIFT" in cs2.modifiers

    # C3 (48) -> '8'
    c3 = mapper.map_pitch(48)
    assert c3 is not None
    assert c3.char == "8"
    assert "SHIFT" not in c3.modifiers

    # F3 (53) -> 'q'
    f3 = mapper.map_pitch(53)
    assert f3 is not None
    assert f3.char == "q"
    assert "SHIFT" not in f3.modifiers

    # F#3 (54) -> 'Shift+q'
    fs3 = mapper.map_pitch(54)
    assert fs3 is not None
    assert fs3.char == "Shift+q"
    assert fs3.physical_key == "q"
    assert "SHIFT" in fs3.modifiers

    # C4 (60, Middle C) -> 't'
    c4 = mapper.map_pitch(60)
    assert c4 is not None
    assert c4.char == "t"
    assert "SHIFT" not in c4.modifiers

    # C#4 (61) -> 'Shift+t'
    cs4 = mapper.map_pitch(61)
    assert cs4 is not None
    assert cs4.char == "Shift+t"
    assert "SHIFT" in cs4.modifiers

    # C5 (72) -> 's'
    c5 = mapper.map_pitch(72)
    assert c5 is not None
    assert c5.char == "s"

    # C6 (84) -> 'l'
    c6 = mapper.map_pitch(84)
    assert c6 is not None
    assert c6.char == "l"

    # C7 (96) -> 'm'
    c7 = mapper.map_pitch(96)
    assert c7 is not None
    assert c7.char == "m"
    assert "SHIFT" not in c7.modifiers


def test_out_of_range_handling():
    mapper = RobloxPianoMapper()
    assert not mapper.can_play(20)  # Below A0
    assert not mapper.can_play(109)  # Above C8
    assert mapper.can_play(21)
    assert mapper.can_play(108)
