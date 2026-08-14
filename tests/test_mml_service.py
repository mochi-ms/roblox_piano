import os
import pytest
import tempfile
import mido

from src.services.mml_service import MmlConversionService
from src.importers.mml_importer import MmlImporter, MmlParseError
from src.library.manager import LibraryManager
from src.utils.config import ConfigManager

def test_mml_service_validation():
    service = MmlConversionService()
    valid, meta, err, pos = service.validate_and_analyze("MML@T120L4CDEFGAB;")
    assert valid is True
    assert meta["total_notes"] == 7
    assert meta["tempo"] == 120
    assert err is None

def test_mml_service_error_reporting():
    service = MmlConversionService()
    valid, meta, err, pos = service.validate_and_analyze("MML@T120 C D X E;")
    assert valid is False
    assert meta is None
    assert "지원하지 않는 토큰" in err or "X" in err
    assert pos is not None

def test_mml_saved_midi_reopens(tmp_path):
    service = MmlConversionService()
    out_file = tmp_path / "service_test.mid"
    stats = service.convert_to_midi_file("MML@T140V12L8O5CDEFEDC;", str(out_file))
    assert os.path.exists(out_file)
    assert stats["file_size"] > 0
    assert stats["note_count"] == 7
    
    mid = mido.MidiFile(str(out_file))
    assert len(mid.tracks) == 1
    assert abs(mid.length - 1.5) < 0.2

def test_mml_service_library_import(tmp_path):
    cfg_mgr = ConfigManager()
    cfg_mgr.config.library_dir = str(tmp_path / "lib")
    manager = LibraryManager(cfg_mgr)
    
    service = MmlConversionService()
    item, timeline, stats = service.import_to_library(
        mml_text="MML@T130L4CDEF;",
        title="My Custom MML",
        folder_id=None,
        manager=manager
    )
    assert item is not None
    assert item.title == "My Custom MML"
    assert os.path.exists(item.filepath)
    assert timeline.total_notes == 4
    assert item.total_notes == 4
    assert item.bpm == 130.0

def test_library_manager_rename(tmp_path):
    cfg_mgr = ConfigManager()
    cfg_mgr.config.library_dir = str(tmp_path / "lib2")
    manager = LibraryManager(cfg_mgr)
    
    service = MmlConversionService()
    item, _, _ = service.import_to_library("MML@T120C4;", "Original", None, manager)
    
    renamed = manager.rename_score(item.id, "Renamed")
    assert renamed.title == "Renamed"
    assert "Renamed" in renamed.filepath
    assert os.path.exists(renamed.filepath)

def test_library_manager_copy_and_move(tmp_path):
    cfg_mgr = ConfigManager()
    cfg_mgr.config.library_dir = str(tmp_path / "lib3")
    manager = LibraryManager(cfg_mgr)
    
    service = MmlConversionService()
    item, _, _ = service.import_to_library("MML@T120C4;", "TestScore", None, manager)
    
    folder = manager.create_folder("TargetFolder")
    manager.move_score(item.id, folder.id)
    moved = manager.db.get_score(item.id)
    assert moved.folder_id == folder.id
    assert "TargetFolder" in moved.filepath
    assert os.path.exists(moved.filepath)
    
    copied = manager.copy_score(item.id, None)
    assert copied.id != item.id
    assert os.path.exists(copied.filepath)
    assert copied.folder_id is None
