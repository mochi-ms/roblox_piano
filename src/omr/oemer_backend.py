"""
Roblox Piano Player - Oemer OMR Backend Adapter
"""
import os
import subprocess
from typing import Optional
from src.omr.backend import BaseOMRBackend, OMRResult, OMRStatus


class OemerBackend(BaseOMRBackend):
    """
    Adapter for Oemer Optical Music Recognition (OMR) engine.
    """

    def __init__(self):
        # We rely on the `oemer` command being available in the current python environment.
        pass

    def is_available(self) -> bool:
        try:
            # check if oemer module is installed
            import oemer
            return True
        except ImportError:
            return False

    def get_install_guide(self) -> str:
        return (
            "Oemer OMR engine was not detected in your Python environment.\n\n"
            "To use Image/PDF score recognition:\n"
            "Run: pip install oemer\n\n"
        )

    def recognize_score(self, image_path: str, output_dir: Optional[str] = None) -> OMRResult:
        if not self.is_available():
            return OMRResult(
                status=OMRStatus.NOT_INSTALLED,
                error_message=self.get_install_guide()
            )

        if not os.path.isfile(image_path):
            return OMRResult(
                status=OMRStatus.INVALID_INPUT,
                error_message=f"Input image not found: {image_path}"
            )

        out_dir = output_dir or os.path.dirname(os.path.abspath(image_path))
        os.makedirs(out_dir, exist_ok=True)

        cmd = [
            "oemer",
            image_path,
            "-o", out_dir
        ]

        try:
            proc = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                timeout=300, # Oemer can take a bit longer on CPU
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)
            )

            base_name = os.path.splitext(os.path.basename(image_path))[0]
            # Oemer outputs <base_name>.musicxml
            musicxml_path = os.path.join(out_dir, f"{base_name}.musicxml")

            if os.path.exists(musicxml_path):
                return OMRResult(
                    status=OMRStatus.SUCCESS,
                    musicxml_path=musicxml_path,
                    raw_output=proc.stdout
                )
            else:
                return OMRResult(
                    status=OMRStatus.PROCESSING_ERROR,
                    error_message=f"Oemer failed to produce MusicXML. Log:\n{proc.stderr or proc.stdout}",
                    raw_output=proc.stdout
                )

        except subprocess.TimeoutExpired:
            return OMRResult(
                status=OMRStatus.PROCESSING_ERROR,
                error_message="OMR process timed out after 300 seconds."
            )
        except Exception as e:
            return OMRResult(
                status=OMRStatus.PROCESSING_ERROR,
                error_message=f"OMR execution error: {str(e)}"
            )
