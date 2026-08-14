import os
import tempfile
import time
from PySide6.QtCore import QThread, Signal

from src.video.youtube_adapter import YoutubeAdapter
from src.video.frame_extractor import FrameExtractor
from src.library.manager import LibraryManager
from src.music.timeline import MusicTimeline

class VideoAnalysisWorker(QThread):
    """
    Background worker that runs the full Video -> MusicXML -> Library pipeline.
    """
    progress = Signal(int, str)
    finished_success = Signal(object)  # ScoreItem
    finished_error = Signal(str)

    def __init__(
        self, 
        source_type: str, 
        source_path: str, 
        library_manager: LibraryManager, 
        temp_dir: str = None
    ):
        super().__init__()
        self.source_type = source_type
        self.source_path = source_path
        self.library_manager = library_manager
        self.temp_dir_obj = None
        
        if temp_dir:
            self.temp_dir = temp_dir
        else:
            self.temp_dir_obj = tempfile.TemporaryDirectory()
            self.temp_dir = self.temp_dir_obj.name
            
        self.is_cancelled = False

    def cancel(self):
        self.is_cancelled = True

    def run(self):
        try:
            video_filepath = self.source_path
            title = "Video Import"

            # 1. Download if YouTube
            if self.source_type == "YOUTUBE":
                self.progress.emit(10, "YouTube 메타데이터 분석 중...")
                adapter = YoutubeAdapter(download_dir=self.temp_dir)
                
                meta = adapter.fetch_metadata(self.source_path)
                if meta:
                    title = meta.get('title', 'YouTube Video')
                
                self.progress.emit(20, "비디오 다운로드 중...")
                def yt_hook(d):
                    if d['status'] == 'downloading':
                        # Convert downloaded bytes to rough percentage (20 to 50%)
                        p = d.get('_percent_str', '0%').replace('%','').strip()
                        try:
                            p_val = float(p)
                            self.progress.emit(int(20 + p_val * 0.3), f"다운로드 중... {p}%")
                        except:
                            pass
                
                video_filepath = adapter.download_video(self.source_path, progress_hook=yt_hook)
                if not video_filepath or not os.path.exists(video_filepath):
                    raise RuntimeError("YouTube 비디오 다운로드 실패")

            if self.is_cancelled:
                raise InterruptedError("사용자 취소됨")

            # 2. Extract Frames
            self.progress.emit(50, "비디오 프레임 추출 중...")
            extractor = FrameExtractor(output_dir=os.path.join(self.temp_dir, "frames"))
            
            def frame_prog(pct, msg):
                # mapped from 50% to 70%
                self.progress.emit(50 + int(pct * 0.2), msg)
                
            frames = extractor.extract_unique_frames(video_filepath, progress_cb=frame_prog)
            
            if not frames:
                raise RuntimeError("추출된 유효 프레임이 없습니다.")

            if self.is_cancelled:
                raise InterruptedError("사용자 취소됨")

            # 3. OMR Analysis (Oemer)
            self.progress.emit(70, "OMR (광학 악보 인식) 처리 중...")
            from src.omr.oemer_backend import OemerBackend
            omr = OemerBackend()
            if not omr.is_available():
                raise RuntimeError("Oemer OMR 엔진이 설치되어 있지 않습니다.")

            # Oemer processes one image and outputs a .musicxml file.
            # We will process the first extracted frame for the E2E test.
            if not frames:
                raise RuntimeError("프레임이 없습니다.")
                
            first_frame = frames[0]
            result = omr.recognize_score(first_frame, self.temp_dir)
            
            if result.status.name != "SUCCESS" or not result.musicxml_path:
                raise RuntimeError(f"OMR 분석 실패: {result.error_message}")
                
            xml_path = result.musicxml_path
            
            if self.is_cancelled:
                raise InterruptedError("사용자 취소됨")

            # 4. Library Registration
            self.progress.emit(90, "라이브러리 등록 중...")
            
            # Use MusicXmlImporter to parse the real generated MusicXML
            from src.importers.musicxml_importer import MusicXmlImporter
            importer = MusicXmlImporter()
            importer.load_file(xml_path)
            
            # MusicXmlImporter doesn't return a timeline directly from load_file, it returns notes
            # But the timeline logic in main_window normally does this. Let's build a timeline.
            real_timeline = MusicTimeline()
            real_timeline.title = title
            
            notes = importer.get_notes()
            if not notes:
                raise RuntimeError("인식된 악보에 음표가 없습니다.")
                
            for note in notes:
                real_timeline.add_note(note)
            real_timeline.sort_events()

            item = self.library_manager.register_generated_score(
                xml_filepath=xml_path,
                title=title,
                source_type=self.source_type,
                source_url=self.source_path,
                timeline=real_timeline
            )
            
            self.progress.emit(100, "완료!")
            self.finished_success.emit(item)

        except InterruptedError as e:
            self.finished_error.emit(str(e))
        except Exception as e:
            self.finished_error.emit(f"파이프라인 오류: {str(e)}")
        finally:
            if self.temp_dir_obj:
                self.temp_dir_obj.cleanup()
