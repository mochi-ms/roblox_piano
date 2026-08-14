"""
Roblox Piano Player - Audiveris OMR Backend Adapter
"""
import os
import shutil
import subprocess
from typing import Optional, List
from src.omr.backend import BaseOMRBackend, OMRResult, OMRStatus


class AudiverisBackend(BaseOMRBackend):
    """
    Adapter for Audiveris Optical Music Recognition (OMR) engine.
    Gracefully handles missing installation without crashing the application.
    """

    COMMON_PATHS: List[str] = [
        "audiveris",
        "audiveris.bat",
        r"C:\Program Files\Audiveris\bin\audiveris.bat",
        r"C:\Program Files (x86)\Audiveris\bin\audiveris.bat",
        os.path.expanduser(r"~\AppData\Local\Programs\Audiveris\bin\audiveris.bat"),
    ]

    def __init__(self, custom_executable_path: Optional[str] = None):
        self.custom_path: Optional[str] = custom_executable_path
        self._executable_path: Optional[str] = self._detect_executable()

    def _detect_executable(self) -> Optional[str]:
        if self.custom_path and os.path.exists(self.custom_path):
            return self.custom_path

        # Check PATH
        which_path = shutil.which("audiveris")
        if which_path:
            return which_path

        # Check common Windows installation paths
        for path in self.COMMON_PATHS:
            if os.path.exists(path):
                return path

        return None

    def is_available(self) -> bool:
        return self._executable_path is not None

    def get_install_guide(self) -> str:
        return (
            "Audiveris OMR engine was not detected on your system.\n\n"
            "To use Image/PDF score recognition:\n"
            "1. Download and install Audiveris (https://github.com/Audiveris/audiveris/releases)\n"
            "2. Make sure Java 17+ is installed.\n"
            "3. Add Audiveris to your PATH or configure the path in Settings.\n\n"
            "MIDI and MusicXML formats can be used directly without Audiveris."
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
            self._executable_path,
            "-batch",
            "-export",
            "-output", out_dir,
            image_path
        ]

        try:
            proc = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                timeout=120,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)
            )

            base_name = os.path.splitext(os.path.basename(image_path))[0]
            # Audiveris outputs <base_name>.mxl or <base_name>.xml
            mxl_path = os.path.join(out_dir, f"{base_name}.mxl")
            xml_path = os.path.join(out_dir, f"{base_name}.xml")
            musicxml_path = os.path.join(out_dir, f"{base_name}.musicxml")

            found_path = None
            for p in (mxl_path, xml_path, musicxml_path):
                if os.path.exists(p):
                    found_path = p
                    break

            if found_path:
                return OMRResult(
                    status=OMRStatus.SUCCESS,
                    musicxml_path=found_path,
                    raw_output=proc.stdout
                )
            else:
                return OMRResult(
                    status=OMRStatus.PROCESSING_ERROR,
                    error_message=f"OMR failed to produce MusicXML. Log:\n{proc.stderr or proc.stdout}",
                    raw_output=proc.stdout
                )

        except subprocess.TimeoutExpired:
            return OMRResult(
                status=OMRStatus.PROCESSING_ERROR,
                error_message="OMR process timed out after 120 seconds."
            )
        except Exception as e:
            return OMRResult(
                status=OMRStatus.PROCESSING_ERROR,
                error_message=f"OMR execution error: {str(e)}"
            )
