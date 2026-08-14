import pytest
from unittest.mock import patch, MagicMock
from src.video.pipeline import VideoAnalysisWorker
from src.library.manager import LibraryManager
from src.utils.config import ConfigManager

@pytest.fixture
def mock_library_manager(tmp_path):
    cfg = ConfigManager(config_dir=str(tmp_path))
    cfg.config.library_dir = str(tmp_path / "lib")
    return LibraryManager(cfg)

@patch("src.importers.musicxml_importer.MusicXmlImporter")
@patch("src.omr.oemer_backend.OemerBackend")
@patch("src.video.pipeline.YoutubeAdapter")
@patch("src.video.pipeline.FrameExtractor")
def test_video_worker_youtube(mock_extractor, mock_yt, mock_omr, mock_importer, mock_library_manager, tmp_path):
    # Mock yt-dlp
    mock_yt_inst = mock_yt.return_value
    mock_yt_inst.fetch_metadata.return_value = {"title": "Test Video", "duration": 10.0}
    dummy_path = str(tmp_path / "dummy.mp4")
    with open(dummy_path, "w") as f:
        f.write("dummy")
    mock_yt_inst.download_video.return_value = dummy_path

    # Mock frame extractor
    mock_ex_inst = mock_extractor.return_value
    mock_ex_inst.extract_unique_frames.return_value = ["frame1.png"]
    
    # Mock OMR
    mock_omr_inst = mock_omr.return_value
    mock_omr_inst.is_available.return_value = True
    
    from src.omr.backend import OMRResult, OMRStatus
    mock_musicxml = str(tmp_path / "result.musicxml")
    with open(mock_musicxml, "w") as f:
        f.write("dummy")
    mock_omr_inst.recognize_score.return_value = OMRResult(status=OMRStatus.SUCCESS, musicxml_path=mock_musicxml)
    
    # Mock Importer
    from src.music.events import NoteEvent
    mock_importer_inst = mock_importer.return_value
    mock_importer_inst.get_notes.return_value = [NoteEvent(pitch=60, start_time=0.0, end_time=1.0)]

    worker = VideoAnalysisWorker(
        source_type="YOUTUBE",
        source_path="http://youtube.com/watch?v=123",
        library_manager=mock_library_manager
    )

    # Collect signals
    success_items = []
    error_msgs = []
    worker.finished_success.connect(lambda item: success_items.append(item))
    worker.finished_error.connect(lambda msg: error_msgs.append(msg))

    worker.run()

    assert not error_msgs
    assert len(success_items) == 1
    assert success_items[0].title == "Test Video"
    assert success_items[0].source_type == "YOUTUBE"

@patch("src.omr.oemer_backend.OemerBackend")
@patch("src.video.pipeline.YoutubeAdapter")
@patch("src.video.pipeline.FrameExtractor")
def test_video_worker_cancel(mock_extractor, mock_yt, mock_omr, mock_library_manager, tmp_path):
    mock_yt_inst = mock_yt.return_value
    mock_yt_inst.fetch_metadata.return_value = {"title": "Test Video", "duration": 10.0}
    dummy_path = str(tmp_path / "dummy.mp4")
    with open(dummy_path, "w") as f:
        f.write("dummy")
    mock_yt_inst.download_video.return_value = dummy_path
    
    # Mock OMR
    mock_omr_inst = mock_omr.return_value
    mock_omr_inst.is_available.return_value = True

    worker = VideoAnalysisWorker(
        source_type="YOUTUBE",
        source_path="http://youtube.com/watch?v=123",
        library_manager=mock_library_manager
    )
    
    error_msgs = []
    worker.finished_error.connect(lambda msg: error_msgs.append(msg))
    
    worker.cancel()
    worker.run()
    
    assert len(error_msgs) == 1
    assert "취소" in error_msgs[0]
