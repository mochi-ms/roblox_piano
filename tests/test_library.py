import os
import tempfile
import pytest
from src.library.models import ScoreItem
from src.library.database import ScoreDatabase
from src.library.manager import LibraryManager
from src.utils.config import AppConfig, ConfigManager
from src.music.timeline import MusicTimeline

@pytest.fixture
def temp_library_dir():
    with tempfile.TemporaryDirectory() as d:
        yield d

@pytest.fixture
def config_manager(temp_library_dir):
    cfg = AppConfig(library_dir=temp_library_dir)
    mgr = ConfigManager(config_dir=temp_library_dir)
    mgr.config = cfg
    return mgr

def test_database_crud(temp_library_dir):
    db_path = os.path.join(temp_library_dir, "test.db")
    db = ScoreDatabase(db_path)
    
    item = ScoreItem(
        id="test-123",
        title="Test Song",
        source_type="FILE",
        source_url="test.mid",
        filepath="test.mid",
        duration=120.0,
        tags="test,song"
    )
    
    # Insert
    db.insert_score(item)
    
    # Read
    fetched = db.get_score("test-123")
    assert fetched is not None
    assert fetched.title == "Test Song"
    assert fetched.get_tags_list() == ["test", "song"]
    
    # Search
    results = db.search_scores("Test")
    assert len(results) == 1
    
    # Update
    item.title = "Updated Song"
    db.update_score(item)
    fetched2 = db.get_score("test-123")
    assert fetched2.title == "Updated Song"
    
    # Delete
    db.delete_score("test-123")
    assert db.get_score("test-123") is None

def test_library_manager_import(config_manager, temp_library_dir):
    mgr = LibraryManager(config_manager)
    
    # Create dummy file
    dummy_source = os.path.join(temp_library_dir, "source.mid")
    with open(dummy_source, "w") as f:
        f.write("dummy")
        
    tl = MusicTimeline()
    tl.title = "My Imported Song"
    from src.music.events import NoteEvent
    tl.add_note(NoteEvent(pitch=60, start_time=0.0, end_time=60.0))
    tl.sort_events()

    item = mgr.import_external_file(dummy_source, source_type="LOCAL")
    mgr.update_score_from_timeline(item.id, tl)
    
    # Refetch to get updated fields
    item = mgr.db.get_score(item.id)

    assert item is not None
    assert item.title == "My Imported Song"
    assert item.tags == "imported"
    
    # Check if copied
    assert os.path.exists(item.filepath)
    
    # Cleanup
    mgr.delete_score(item.id)
    assert not os.path.exists(item.filepath)
