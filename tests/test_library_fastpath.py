import os
import time

from src.library.database import ScoreDatabase
from src.library.manager import LibraryManager
from src.library.models import ScoreItem
from src.utils.config import ConfigManager


def _make_manager(tmp_path):
    cfg = ConfigManager()
    cfg.config.library_dir = str(tmp_path)
    return LibraryManager(cfg)


def _insert_score(db, score_id, title, folder_id=None, favorite=False, last_played=0.0):
    db.insert_score(
        ScoreItem(
            id=score_id,
            title=title,
            source_type="MIDI",
            source_url=f"{title}.mid",
            filepath=f"{title}.mid",
            original_filename=f"{title}.mid",
            file_extension=".mid",
            folder_id=folder_id,
            duration=60.0,
            bpm=120.0,
            total_notes=100,
            favorite=favorite,
            last_played_at=last_played,
        )
    )


def test_library_indexes_created(tmp_path):
    db = ScoreDatabase(str(tmp_path / "library.db"))
    indexes = db.get_index_names()
    assert "idx_scores_folder_created" in indexes
    assert "idx_scores_folder_title" in indexes
    assert "idx_folders_parent_name" in indexes


def test_folder_query_is_filtered_and_paged(tmp_path):
    mgr = _make_manager(tmp_path)
    folder_a = mgr.create_folder("A")
    folder_b = mgr.create_folder("B")

    for i in range(300):
        _insert_score(mgr.db, f"a-{i}", f"A {i:03}", folder_a.id)
    for i in range(300):
        _insert_score(mgr.db, f"b-{i}", f"B {i:03}", folder_b.id)

    page = mgr.get_folder_scores(
        folder_a.id,
        limit=25,
        offset=50,
        sort_by="title",
        descending=False,
    )

    assert len(page) == 25
    assert all(score.folder_id == folder_a.id for score in page)
    assert page[0].title == "A 050"
    assert mgr.count_folder_scores(folder_a.id) == 300


def test_favorites_and_recent_are_server_side_queries(tmp_path):
    mgr = _make_manager(tmp_path)
    now = time.time()

    _insert_score(mgr.db, "1", "Normal")
    _insert_score(mgr.db, "2", "Favorite", favorite=True)
    _insert_score(mgr.db, "3", "Recent Old", last_played=now - 100)
    _insert_score(mgr.db, "4", "Recent New", last_played=now - 10)

    favorites = mgr.get_favorites(limit=20)
    recent = mgr.get_recent(limit=20)

    assert [s.id for s in favorites] == ["2"]
    assert [s.id for s in recent][:2] == ["4", "3"]


def test_folder_path_cache_stays_correct_after_rename(tmp_path):
    mgr = _make_manager(tmp_path)
    parent = mgr.create_folder("Parent")
    child = mgr.create_folder("Child", parent.id)

    before = mgr._get_folder_path(child.id)
    assert before.endswith(os.path.join("Parent", "Child"))

    mgr.rename_folder(parent.id, "Renamed")
    after = mgr._get_folder_path(child.id)

    assert after.endswith(os.path.join("Renamed", "Child"))


def test_clean_spurious_empty_folders_does_not_remove_real_folder(tmp_path):
    mgr = _make_manager(tmp_path)
    real_folder = mgr.create_folder("Real")
    assert os.path.isdir(mgr._get_folder_path(real_folder.id))

    cleaned = mgr.clean_spurious_empty_folders()

    assert cleaned == 0
    assert mgr.db.get_folder(real_folder.id) is not None


def test_copy_folder_recursively_preserves_registered_scores(tmp_path):
    mgr = _make_manager(tmp_path)
    source = mgr.create_folder("Source")
    nested = mgr.create_folder("Nested", source.id)

    score_path = os.path.join(mgr._get_folder_path(nested.id), "song.mid")
    with open(score_path, "wb") as file_obj:
        file_obj.write(b"MThd")

    _insert_score(mgr.db, "source-score", "song", nested.id)
    score = mgr.db.get_score("source-score")
    score.filepath = score_path
    score.original_filename = "song.mid"
    mgr.db.update_score(score)

    copied_root = mgr.copy_folder(source.id, None)
    copied_children = mgr.get_child_folders(copied_root.id)
    assert len(copied_children) == 1

    copied_scores = mgr.get_folder_scores(copied_children[0].id)
    assert len(copied_scores) == 1
    assert os.path.exists(copied_scores[0].filepath)
