import os
import tempfile
import pytest
from src.utils.config import ConfigManager
from src.library.manager import LibraryManager
from src.library.models import ScoreItem, FolderItem


@pytest.fixture
def temp_lib():
    with tempfile.TemporaryDirectory() as tmpdir:
        cfg = ConfigManager()
        cfg.config.library_dir = tmpdir
        mgr = LibraryManager(cfg)
        yield mgr, tmpdir


def test_internal_score_move(temp_lib):
    mgr, tmpdir = temp_lib
    
    # Create folder A and B
    folder_a = mgr.create_folder("FolderA")
    folder_b = mgr.create_folder("FolderB")
    
    # Create synthetic midi file in folder A
    file_path = os.path.join(mgr._get_folder_path(folder_a.id), "song.mid")
    with open(file_path, "wb") as f:
        f.write(b"MThd\x00\x00\x00\x06\x00\x00\x00\x01\x01\xe0")
        
    score = mgr.import_external_file(file_path, folder_id=folder_a.id)
    assert score.folder_id == folder_a.id
    assert os.path.exists(score.filepath)
    assert "FolderA" in score.filepath
    
    # Move score to folder B
    mgr.move_score(score.id, folder_b.id)
    
    updated_score = mgr.db.get_score(score.id)
    assert updated_score.folder_id == folder_b.id
    assert os.path.exists(updated_score.filepath)
    assert "FolderB" in updated_score.filepath
    assert not os.path.exists(score.filepath)


def test_internal_folder_move(temp_lib):
    mgr, tmpdir = temp_lib
    
    # Create Parent and Child folders
    parent = mgr.create_folder("Classical")
    child = mgr.create_folder("Chopin")
    
    # Add score inside Chopin
    chopin_dir = mgr._get_folder_path(child.id)
    score_file = os.path.join(chopin_dir, "nocturne.mid")
    with open(score_file, "wb") as f:
        f.write(b"MThd\x00\x00\x00\x06\x00\x00\x00\x01\x01\xe0")
    score = mgr.import_external_file(score_file, folder_id=child.id)
    
    # Move Chopin into Classical
    mgr.move_folder(child.id, parent.id)
    
    updated_child = mgr.db.get_folder(child.id)
    assert updated_child.parent_id == parent.id
    
    # Check physical paths
    new_chopin_path = mgr._get_folder_path(child.id)
    assert os.path.exists(new_chopin_path)
    assert os.path.join("Classical", "Chopin") in new_chopin_path
    
    # Check updated score path
    updated_score = mgr.db.get_score(score.id)
    assert os.path.exists(updated_score.filepath)
    assert os.path.join("Classical", "Chopin", "nocturne.mid") in updated_score.filepath


def test_prevent_folder_cycle(temp_lib):
    mgr, tmpdir = temp_lib
    
    # Create hierarchy: Root -> A -> B -> C
    folder_a = mgr.create_folder("A")
    folder_b = mgr.create_folder("B", parent_id=folder_a.id)
    folder_c = mgr.create_folder("C", parent_id=folder_b.id)
    
    # 1. Cannot move A into A
    with pytest.raises(ValueError):
        mgr.move_folder(folder_a.id, folder_a.id)
        
    # 2. Cannot move A into B (child)
    with pytest.raises(ValueError):
        mgr.move_folder(folder_a.id, folder_b.id)
        
    # 3. Cannot move A into C (descendant)
    with pytest.raises(ValueError):
        mgr.move_folder(folder_a.id, folder_c.id)
        
    # 4. Moving C into Root (None) is valid
    mgr.move_folder(folder_c.id, None)
    updated_c = mgr.db.get_folder(folder_c.id)
    assert updated_c.parent_id is None


def test_clean_spurious_empty_folders(temp_lib):
    mgr, tmpdir = temp_lib
    
    # Insert a ghost folder directly into DB without physical folder
    ghost_folder = FolderItem(id="ghost-123", name="v", parent_id=None)
    mgr.db.insert_folder(ghost_folder)
    
    # Normal folder with physical dir
    normal_folder = mgr.create_folder("RealFolder")
    
    assert len(mgr.get_all_folders()) == 2
    
    cleaned = mgr.clean_spurious_empty_folders()
    assert cleaned == 1
    
    remaining = mgr.get_all_folders()
    assert len(remaining) == 1
    assert remaining[0].name == "RealFolder"
