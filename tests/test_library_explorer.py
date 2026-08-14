import os
import pytest
import tempfile

from src.library.manager import LibraryManager
from src.utils.config import ConfigManager
from src.services.mml_service import MmlConversionService

def test_score_physical_rename(tmp_path):
    cfg = ConfigManager()
    cfg.config.library_dir = str(tmp_path)
    mgr = LibraryManager(cfg)
    
    svc = MmlConversionService()
    item, _, _ = svc.import_to_library("MML@T120C4;", "OriginalSong", None, mgr)
    assert os.path.exists(item.filepath)
    
    renamed = mgr.rename_score(item.id, "RenamedSong")
    assert renamed.title == "RenamedSong"
    assert os.path.exists(renamed.filepath)
    assert "OriginalSong.mid" not in renamed.filepath
    assert "RenamedSong.mid" in renamed.filepath

def test_folder_physical_rename(tmp_path):
    cfg = ConfigManager()
    cfg.config.library_dir = str(tmp_path)
    mgr = LibraryManager(cfg)
    
    folder = mgr.create_folder("OldFolder")
    svc = MmlConversionService()
    item, _, _ = svc.import_to_library("MML@T120C4;", "InFolderSong", folder.id, mgr)
    assert os.path.exists(item.filepath)
    
    renamed_folder = mgr.rename_folder(folder.id, "NewFolder")
    assert renamed_folder.name == "NewFolder"
    
    updated_item = mgr.db.get_score(item.id)
    assert "NewFolder" in updated_item.filepath
    assert os.path.exists(updated_item.filepath)

def test_copy_score(tmp_path):
    cfg = ConfigManager()
    cfg.config.library_dir = str(tmp_path)
    mgr = LibraryManager(cfg)
    
    svc = MmlConversionService()
    item, _, _ = svc.import_to_library("MML@T120C4;", "BaseScore", None, mgr)
    
    copied = mgr.copy_score(item.id, None)
    assert copied.id != item.id
    assert os.path.exists(copied.filepath)
    assert os.path.exists(item.filepath)
    assert "BaseScore (1).mid" in copied.filepath

def test_move_score(tmp_path):
    cfg = ConfigManager()
    cfg.config.library_dir = str(tmp_path)
    mgr = LibraryManager(cfg)
    
    folder1 = mgr.create_folder("Folder1")
    folder2 = mgr.create_folder("Folder2")
    
    svc = MmlConversionService()
    item, _, _ = svc.import_to_library("MML@T120C4;", "MovingSong", folder1.id, mgr)
    assert "Folder1" in item.filepath
    
    mgr.move_score(item.id, folder2.id)
    moved = mgr.db.get_score(item.id)
    assert moved.folder_id == folder2.id
    assert "Folder2" in moved.filepath
    assert os.path.exists(moved.filepath)

def test_delete_score(tmp_path):
    cfg = ConfigManager()
    cfg.config.library_dir = str(tmp_path)
    mgr = LibraryManager(cfg)
    
    svc = MmlConversionService()
    item, _, _ = svc.import_to_library("MML@T120C4;", "DeleteSong", None, mgr)
    assert os.path.exists(item.filepath)
    
    mgr.delete_score(item.id, permanent=True)
    assert not os.path.exists(item.filepath)
    assert mgr.db.get_score(item.id) is None
