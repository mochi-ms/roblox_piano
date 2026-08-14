import os
import shutil
import pytest
import mido
import tempfile

from src.library.manager import LibraryManager
from src.utils.config import ConfigManager

def _create_dummy_midi(filepath, bpm=120, note_count=4):
    mid = mido.MidiFile(ticks_per_beat=480)
    track = mido.MidiTrack()
    mid.tracks.append(track)
    track.append(mido.MetaMessage('set_tempo', tempo=mido.bpm2tempo(bpm), time=0))
    for i in range(note_count):
        track.append(mido.Message('note_on', note=60 + i, velocity=100, time=0))
        track.append(mido.Message('note_off', note=60 + i, velocity=0, time=240))
    track.append(mido.MetaMessage('end_of_track', time=0))
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    mid.save(filepath)

def test_import_folder_preserves_tree(tmp_path):
    # 1. Setup source directory tree
    src_root = tmp_path / "SourceScores"
    os.makedirs(src_root / "Classical" / "Mozart", exist_ok=True)
    os.makedirs(src_root / "Classical" / "Chopin", exist_ok=True)
    os.makedirs(src_root / "Anime", exist_ok=True)
    
    _create_dummy_midi(str(src_root / "Classical" / "Mozart" / "TurkishMarch.mid"), bpm=140, note_count=10)
    _create_dummy_midi(str(src_root / "Classical" / "Chopin" / "Nocturne.mid"), bpm=90, note_count=8)
    _create_dummy_midi(str(src_root / "Anime" / "Theme.mid"), bpm=120, note_count=5)
    
    # Text file that is not a score
    with open(src_root / "README.txt", "w", encoding="utf-8") as f:
        f.write("Some readme instructions")
        
    # MML score
    with open(src_root / "Anime" / "Song.mml", "w", encoding="utf-8") as f:
        f.write("MML@T130L4CDEF;")

    # 2. Setup library target
    lib_dir = tmp_path / "LibraryTarget"
    cfg = ConfigManager()
    cfg.config.library_dir = str(lib_dir)
    mgr = LibraryManager(cfg)

    # 3. Perform recursive folder import
    summary = mgr.import_folder_recursive(str(src_root), target_parent_folder_id=None)
    
    assert summary["imported_scores"] == 4 # 3 mid + 1 mml
    assert summary["skipped"] == 1 # README.txt
    assert summary["failed"] == 0
    assert summary["cancelled"] is False

    # 4. Verify physical directory tree
    assert os.path.exists(lib_dir / "SourceScores" / "Classical" / "Mozart" / "TurkishMarch.mid")
    assert os.path.exists(lib_dir / "SourceScores" / "Classical" / "Chopin" / "Nocturne.mid")
    assert os.path.exists(lib_dir / "SourceScores" / "Anime" / "Theme.mid")
    assert os.path.exists(lib_dir / "SourceScores" / "Anime" / "Song.mml")
    assert not os.path.exists(lib_dir / "SourceScores" / "README.txt")

    # 5. Verify DB folder and score hierarchy
    scores = mgr.db.get_all_scores()
    folders = mgr.db.get_all_folders()
    
    folder_by_name = {f.name: f for f in folders}
    assert "SourceScores" in folder_by_name
    assert "Classical" in folder_by_name
    assert "Mozart" in folder_by_name
    assert "Chopin" in folder_by_name
    assert "Anime" in folder_by_name

    assert folder_by_name["Classical"].parent_id == folder_by_name["SourceScores"].id
    assert folder_by_name["Mozart"].parent_id == folder_by_name["Classical"].id
    assert folder_by_name["Chopin"].parent_id == folder_by_name["Classical"].id
    assert folder_by_name["Anime"].parent_id == folder_by_name["SourceScores"].id

    score_by_orig = {s.original_filename: s for s in scores}
    assert score_by_orig["TurkishMarch.mid"].folder_id == folder_by_name["Mozart"].id
    assert score_by_orig["TurkishMarch.mid"].total_notes == 10
    assert score_by_orig["Nocturne.mid"].folder_id == folder_by_name["Chopin"].id
    assert score_by_orig["Nocturne.mid"].total_notes == 8

    # 6. Verify original source files are intact (NOT moved)
    assert os.path.exists(src_root / "Classical" / "Mozart" / "TurkishMarch.mid")
    assert os.path.exists(src_root / "README.txt")

def test_import_folder_nested_structure(tmp_path):
    src_root = tmp_path / "DeepRoot"
    deep_path = src_root / "Level1" / "Level2" / "Level3" / "Level4" / "Level5"
    os.makedirs(deep_path, exist_ok=True)
    _create_dummy_midi(str(deep_path / "DeepSong.mid"), bpm=120, note_count=6)

    lib_dir = tmp_path / "LibraryTarget2"
    cfg = ConfigManager()
    cfg.config.library_dir = str(lib_dir)
    mgr = LibraryManager(cfg)

    summary = mgr.import_folder_recursive(str(src_root))
    assert summary["imported_scores"] == 1
    assert os.path.exists(lib_dir / "DeepRoot" / "Level1" / "Level2" / "Level3" / "Level4" / "Level5" / "DeepSong.mid")

def test_import_folder_empty_dirs(tmp_path):
    src_root = tmp_path / "EmptyRoot"
    os.makedirs(src_root / "EmptySub1" / "EmptySub2", exist_ok=True)

    lib_dir = tmp_path / "LibraryTarget3"
    cfg = ConfigManager()
    cfg.config.library_dir = str(lib_dir)
    mgr = LibraryManager(cfg)

    summary = mgr.import_folder_recursive(str(src_root))
    assert summary["imported_scores"] == 0
    assert os.path.exists(lib_dir / "EmptyRoot" / "EmptySub1" / "EmptySub2")

def test_import_folder_duplicate_collision_safe(tmp_path):
    src_root = tmp_path / "DupRoot"
    os.makedirs(src_root / "Folder", exist_ok=True)
    _create_dummy_midi(str(src_root / "Folder" / "Song.mid"), bpm=120, note_count=3)

    lib_dir = tmp_path / "LibraryTarget4"
    cfg = ConfigManager()
    cfg.config.library_dir = str(lib_dir)
    mgr = LibraryManager(cfg)

    # First import
    mgr.import_folder_recursive(str(src_root))
    assert os.path.exists(lib_dir / "DupRoot" / "Folder" / "Song.mid")

    # Second import of the same folder (should merge folder and safely name duplicate file)
    summary2 = mgr.import_folder_recursive(str(src_root))
    assert summary2["imported_scores"] == 1
    assert os.path.exists(lib_dir / "DupRoot" / "Folder" / "Song (1).mid")
