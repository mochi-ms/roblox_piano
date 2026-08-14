"""
Roblox Piano Player - Image Score Importer (OpenCV Preprocessing + OMR)
"""
import os
import cv2
import numpy as np
from typing import List, Optional
from src.importers.base import BaseMusicImporter
from src.importers.musicxml_importer import MusicXmlImporter
from src.music.timeline import MusicTimeline
from src.omr.backend import BaseOMRBackend, OMRStatus
from src.omr.audiveris_backend import AudiverisBackend


class ImageImporter(BaseMusicImporter):
    """
    Imports score images (PNG, JPG, JPEG) by preprocessing with OpenCV
    and executing Optical Music Recognition (OMR) to generate a MusicTimeline.
    """

    def __init__(self, omr_backend: Optional[BaseOMRBackend] = None):
        self.omr_backend: BaseOMRBackend = omr_backend or AudiverisBackend()
        self.musicxml_importer = MusicXmlImporter()

    @property
    def supported_extensions(self) -> List[str]:
        return [".png", ".jpg", ".jpeg", ".bmp", ".tiff"]

    def can_import(self, file_path_or_content: str) -> bool:
        if not os.path.isfile(file_path_or_content):
            return False
        ext = os.path.splitext(file_path_or_content)[1].lower()
        return ext in self.supported_extensions

    def preprocess_image(self, image_path: str, output_path: Optional[str] = None) -> str:
        """
        Applies OpenCV preprocessing (Grayscale, Contrast enhancement, Deskew, Thresholding).
        """
        img = cv2.imread(image_path)
        if img is None:
            raise ValueError(f"Could not load image: {image_path}")

        # 1. Grayscale
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

        # 2. Denoise and adaptive threshold
        blurred = cv2.GaussianBlur(gray, (3, 3), 0)
        binary = cv2.adaptiveThreshold(
            blurred, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 11, 2
        )

        # 3. Deskew detection using Hough Lines
        edges = cv2.Canny(binary, 50, 150, apertureSize=3)
        lines = cv2.HoughLinesP(edges, 1, np.pi / 180, 100, minLineLength=100, maxLineGap=10)

        angle = 0.0
        if lines is not None:
            angles = []
            for line in lines:
                x1, y1, x2, y2 = line[0]
                if abs(x2 - x1) > 0.001:
                    deg = np.degrees(np.arctan2(y2 - y1, x2 - x1))
                    if -45 < deg < 45:
                        angles.append(deg)
            if angles:
                angle = float(np.median(angles))

        # Rotate if deskew angle is noticeable (> 0.5 degrees)
        h, w = gray.shape[:2]
        if abs(angle) > 0.5:
            center = (w // 2, h // 2)
            rot_mat = cv2.getRotationMatrix2D(center, angle, 1.0)
            gray = cv2.warpAffine(gray, rot_mat, (w, h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)

        # 4. Save preprocessed image
        if not output_path:
            base, ext = os.path.splitext(image_path)
            output_path = f"{base}_preprocessed.png"

        cv2.imwrite(output_path, gray)
        return output_path

    def import_score(self, file_path_or_content: str, **kwargs) -> MusicTimeline:
        if not os.path.isfile(file_path_or_content):
            raise FileNotFoundError(f"Image score file not found: {file_path_or_content}")

        if not self.omr_backend.is_available():
            raise RuntimeError(
                f"OMR Engine (Audiveris) is not installed.\n\n"
                f"To recognize sheet music images, please install Audiveris.\n"
                f"Directly supported formats without OMR: MIDI (.mid), MusicXML (.musicxml, .xml, .mxl), Numeric (.txt)"
            )

        # Preprocess
        preprocessed_path = self.preprocess_image(file_path_or_content)

        # Run OMR
        result = self.omr_backend.recognize_score(preprocessed_path)
        if result.status != OMRStatus.SUCCESS or not result.musicxml_path:
            raise RuntimeError(f"OMR Recognition failed: {result.error_message}")

        # Parse generated MusicXML
        timeline = self.musicxml_importer.import_score(result.musicxml_path)
        timeline.title = os.path.splitext(os.path.basename(file_path_or_content))[0]
        return timeline
