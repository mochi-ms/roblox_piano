import os
import sys
import subprocess
import shutil
import cv2
import numpy as np

# Add project root to sys.path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '../../')))

from src.video.frame_extractor import FrameExtractor
from src.omr.oemer_backend import OemerBackend
from src.importers.musicxml_importer import MusicXmlImporter
from src.music.timeline import MusicTimeline
from src.library.manager import LibraryManager
from src.utils.config import ConfigManager

def generate_mock_video(video_path: str):
    """Generates a 1-second video of a very simple mock sheet music (so Oemer can try to parse it, even if it finds 0 notes, it runs).
       Actually, oemer will fail or return empty if it's not real music.
       We will draw a staff with a note."""
    height, width = 600, 800
    fps = 30
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    out = cv2.VideoWriter(video_path, fourcc, fps, (width, height))

    # Create a white background image
    img = np.ones((height, width, 3), dtype=np.uint8) * 255

    # Draw staff lines
    for i in range(5):
        y = 200 + i * 20
        cv2.line(img, (100, y), (700, y), (0, 0, 0), 2)
    
    # Draw a note
    cv2.circle(img, (300, 240), 10, (0, 0, 0), -1) # Notehead
    cv2.line(img, (310, 240), (310, 160), (0, 0, 0), 2) # Stem

    for _ in range(fps):
        out.write(img)

    out.release()

def run_e2e():
    results = {
        "VIDEO_MODULE_EXISTS": "FALSE",
        "LIBRARY_MODULE_EXISTS": "FALSE",
        "YOUTUBE_ACQUISITION": "SKIPPED", # Local MP4 fallback used
        "LOCAL_VIDEO_E2E": "FAIL",
        "SCORE_RECONSTRUCTION": "FAIL",
        "OMR_BACKEND": "oemer",
        "OMR_RUNTIME": "FAIL",
        "MUSICXML_FILE_CREATED": "FALSE",
        "MUSICXML_FILE_PATH": "",
        "PARSED_NOTE_COUNT": "0",
        "TIMELINE_CREATED": "FALSE",
        "LIBRARY_ITEM_CREATED": "FALSE",
        "LIBRARY_REOPEN": "FALSE",
        "PYTEST_PASS": "0",
        "PYTEST_FAIL": "0",
        "LOCAL_HEAD": "",
        "REMOTE_HEAD": "",
        "FINAL_RESULT": "NEEDS_MORE_WORK"
    }

    # 1. Check directories
    if os.path.exists("src/video"):
        results["VIDEO_MODULE_EXISTS"] = "TRUE"
    if os.path.exists("src/library"):
        results["LIBRARY_MODULE_EXISTS"] = "TRUE"

    # 2. Check Git HEADs
    try:
        local_head = subprocess.check_output(["git", "rev-parse", "HEAD"]).decode().strip()
        results["LOCAL_HEAD"] = local_head
        remote_head = subprocess.check_output(["git", "ls-remote", "origin", "HEAD"]).decode().split()[0]
        results["REMOTE_HEAD"] = remote_head
    except:
        pass

    temp_dir = "tools/validation/temp_e2e"
    os.makedirs(temp_dir, exist_ok=True)
    video_path = os.path.join(temp_dir, "test.mp4")

    try:
        # Generate Local MP4
        generate_mock_video(video_path)
        
        # Extract Frames
        extractor = FrameExtractor(os.path.join(temp_dir, "frames"))
        frames = extractor.extract_unique_frames(video_path, diff_threshold=5.0)
        
        if frames:
            results["SCORE_RECONSTRUCTION"] = "SUCCESS"
        else:
            raise RuntimeError("No frames extracted")

        # OMR
        omr = OemerBackend()
        if omr.is_available():
            result = omr.recognize_score(frames[0], temp_dir)
            if result.status.name == "SUCCESS" and result.musicxml_path:
                results["OMR_RUNTIME"] = "SUCCESS"
                results["MUSICXML_FILE_CREATED"] = "TRUE"
                results["MUSICXML_FILE_PATH"] = result.musicxml_path
                
                # Parse MusicXML
                importer = MusicXmlImporter()
                try:
                    importer.load_file(result.musicxml_path)
                    notes = importer.get_notes()
                    results["PARSED_NOTE_COUNT"] = str(len(notes))
                    
                    timeline = MusicTimeline()
                    for n in notes:
                        timeline.add_note(n)
                    timeline.sort_events()
                    results["TIMELINE_CREATED"] = "TRUE"
                    
                    # Save to Library
                    config = ConfigManager(temp_dir)
                    lib_mgr = LibraryManager(config)
                    item = lib_mgr.register_generated_score(
                        xml_filepath=result.musicxml_path,
                        title="E2E Test",
                        source_type="LOCAL",
                        source_url=video_path,
                        timeline=timeline
                    )
                    
                    if item:
                        results["LIBRARY_ITEM_CREATED"] = "TRUE"
                        
                        # Reopen from Library
                        reopened_item = lib_mgr.get_score(item.id)
                        if reopened_item:
                            results["LIBRARY_REOPEN"] = "TRUE"
                            results["LOCAL_VIDEO_E2E"] = "SUCCESS"
                except Exception as e:
                    print(f"Error parsing/saving: {e}")
        
    except Exception as e:
        print(f"E2E Error: {e}")

    # Run pytest
    try:
        pytest_out = subprocess.check_output([sys.executable, "-m", "pytest", "-q"], text=True)
        # Parse pytest out roughly
        pass_count = pytest_out.count("PASSED") + pytest_out.count(".")
        results["PYTEST_PASS"] = str(pass_count) # Just a rough estimate for script, actual count below
    except subprocess.CalledProcessError as e:
        results["PYTEST_FAIL"] = str(e.output.count("FAILED") + e.output.count("F"))
    
    # We will grab actual pytest numbers by running pytest normally before printing
    proc = subprocess.run([sys.executable, "-m", "pytest", "--tb=no"], capture_output=True, text=True)
    out = proc.stdout
    import re
    passed = re.search(r'(\d+) passed', out)
    failed = re.search(r'(\d+) failed', out)
    results["PYTEST_PASS"] = passed.group(1) if passed else "0"
    results["PYTEST_FAIL"] = failed.group(1) if failed else "0"

    if results["LOCAL_VIDEO_E2E"] == "SUCCESS" and results["PYTEST_FAIL"] == "0":
        results["FINAL_RESULT"] = "READY_FOR_REAL_VIDEO_TEST"

    # Print Report
    print("================== FINAL REPORT ==================")
    for k, v in results.items():
        print(f"{k}={v}")
    print("==================================================")

if __name__ == "__main__":
    run_e2e()
