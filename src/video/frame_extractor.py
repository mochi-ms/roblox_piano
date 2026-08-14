import cv2
import os
import numpy as np
from typing import List, Callable

class FrameExtractor:
    """
    Extracts relevant frames from a video.
    Filters out duplicates or stationary frames to only send unique sheet music pages to OMR.
    """
    def __init__(self, output_dir: str):
        self.output_dir = output_dir
        os.makedirs(self.output_dir, exist_ok=True)

    def extract_unique_frames(
        self, 
        video_path: str, 
        fps_sample: float = 1.0, 
        diff_threshold: float = 5.0,
        progress_cb: Callable[[int, str], None] = None
    ) -> List[str]:
        """
        Samples frames at `fps_sample` rate.
        Saves the frame if it differs from the last saved frame by more than `diff_threshold` %.
        """
        if not os.path.exists(video_path):
            raise FileNotFoundError(f"Video file not found: {video_path}")

        cap = cv2.VideoCapture(video_path)
        if not cap.isOpened():
            raise RuntimeError(f"Could not open video file: {video_path}")

        fps = cap.get(cv2.CAP_PROP_FPS)
        total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        
        if fps <= 0:
            fps = 30.0

        frame_interval = int(fps / fps_sample)
        if frame_interval < 1:
            frame_interval = 1

        saved_files = []
        last_saved_gray = None
        frame_idx = 0
        saved_count = 0

        while True:
            ret, frame = cap.read()
            if not ret:
                break

            if frame_idx % frame_interval == 0:
                # Convert to grayscale for diffing
                gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
                
                # Check diff
                should_save = False
                if last_saved_gray is None:
                    should_save = True
                else:
                    # Calculate absolute difference
                    diff = cv2.absdiff(gray, last_saved_gray)
                    mean_diff = np.mean(diff)
                    if mean_diff > diff_threshold:
                        should_save = True

                if should_save:
                    out_path = os.path.join(self.output_dir, f"frame_{saved_count:04d}.png")
                    cv2.imwrite(out_path, frame)
                    saved_files.append(out_path)
                    last_saved_gray = gray
                    saved_count += 1

            frame_idx += 1
            if progress_cb and frame_idx % (fps * 5) == 0:  # Update progress every 5 seconds of video
                percent = int((frame_idx / total_frames) * 100)
                progress_cb(percent, f"프레임 추출 중... ({percent}%)")

        cap.release()
        return saved_files
