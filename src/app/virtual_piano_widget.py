"""
Roblox Piano Player - 88-Key Virtual Piano Keyboard Visualizer
"""
from typing import Optional, Set, Dict, Tuple
from PySide6.QtCore import Qt, QRectF, Signal
from PySide6.QtGui import QPainter, QColor, QPen, QBrush, QFont, QMouseEvent
from PySide6.QtWidgets import QWidget
from src.piano.mapper import RobloxPianoMapper
from src.piano.profile import KeyMapping


class VirtualPianoWidget(QWidget):
    """
    Renders an interactive 88-key Roblox Virtual Piano (A0 to C8).
    Displays note names and Roblox keyboard key bindings on each key.
    Lights up active notes in real-time during playback.
    """
    key_clicked = Signal(int, KeyMapping)

    def __init__(self, mapper: Optional[RobloxPianoMapper] = None, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.mapper: RobloxPianoMapper = mapper or RobloxPianoMapper()
        self._active_pitches: Set[int] = set()
        self.setMinimumHeight(110)
        self.setMaximumHeight(160)
        self.setMouseTracking(True)
        self._hovered_pitch: Optional[int] = None

    def set_mapper(self, mapper: RobloxPianoMapper) -> None:
        self.mapper = mapper
        self.update()

    def set_active_pitches(self, pitches: Set[int]) -> None:
        self._active_pitches = set(pitches)
        self.update()

    def _get_key_geometry(self) -> Tuple[Dict[int, QRectF], Dict[int, QRectF]]:
        """
        Calculates bounding boxes for 52 white keys and 36 black keys.
        Returns (white_keys_rects, black_keys_rects)
        """
        w = self.width() - 8
        h = self.height() - 8
        top_offset = 4
        left_offset = 4

        num_white_keys = 52
        white_key_width = w / num_white_keys
        white_key_height = h
        black_key_width = white_key_width * 0.65
        black_key_height = white_key_height * 0.60

        white_rects: Dict[int, QRectF] = {}
        black_rects: Dict[int, QRectF] = {}

        # 88 keys: A0 (21) to C8 (108)
        white_index = 0

        for pitch in range(21, 109):
            note_in_octave = pitch % 12
            is_black = note_in_octave in (1, 3, 6, 8, 10)  # C#, D#, F#, G#, A#

            if not is_black:
                # White key
                rect = QRectF(
                    left_offset + white_index * white_key_width,
                    top_offset,
                    white_key_width - 1.0,
                    white_key_height
                )
                white_rects[pitch] = rect
                white_index += 1
            else:
                # Black key sits between (white_index - 1) and white_index
                center_x = left_offset + white_index * white_key_width
                rect = QRectF(
                    center_x - (black_key_width / 2.0),
                    top_offset,
                    black_key_width,
                    black_key_height
                )
                black_rects[pitch] = rect

        return white_rects, black_rects

    def mousePressEvent(self, event: QMouseEvent) -> None:
        if event.button() == Qt.LeftButton:
            pitch = self._get_pitch_at_pos(event.position().x(), event.position().y())
            if pitch is not None:
                km = self.mapper.map_pitch(pitch)
                if km:
                    self.key_clicked.emit(pitch, km)

    def _get_pitch_at_pos(self, x: float, y: float) -> Optional[int]:
        white_rects, black_rects = self._get_key_geometry()

        # Check black keys first (they sit on top)
        for pitch, rect in black_rects.items():
            if rect.contains(x, y):
                return pitch

        # Then check white keys
        for pitch, rect in white_rects.items():
            if rect.contains(x, y):
                return pitch

        return None

    def paintEvent(self, event) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)

        w = self.width()
        h = self.height()

        # Background frame
        painter.fillRect(0, 0, w, h, QColor("#111317"))

        white_rects, black_rects = self._get_key_geometry()

        font_label = QFont("Segoe UI", 8, QFont.Bold)
        font_note = QFont("Segoe UI", 7)

        # 1. Draw White Keys
        for pitch, rect in white_rects.items():
            is_active = (pitch in self._active_pitches)
            km = self.mapper.map_pitch(pitch)

            if is_active:
                brush = QBrush(QColor("#3875E8"))  # Active Blue
                pen = QPen(QColor("#5C95FF"), 1)
                text_color = QColor("#FFFFFF")
            else:
                brush = QBrush(QColor("#EDEFEF"))  # Ivory White
                pen = QPen(QColor("#A0A4AB"), 1)
                text_color = QColor("#1C2026")

            painter.setBrush(brush)
            painter.setPen(pen)
            painter.drawRoundedRect(rect, 3, 3)

            # Draw Roblox Key label
            if km:
                painter.setPen(text_color)
                painter.setFont(font_label)
                painter.drawText(
                    QRectF(rect.x(), rect.bottom() - 28, rect.width(), 14),
                    Qt.AlignCenter,
                    km.char
                )
                painter.setFont(font_note)
                painter.setPen(QColor("#505866") if not is_active else QColor("#DCE7FE"))
                painter.drawText(
                    QRectF(rect.x(), rect.bottom() - 14, rect.width(), 12),
                    Qt.AlignCenter,
                    km.name
                )

        # 2. Draw Black Keys
        for pitch, rect in black_rects.items():
            is_active = (pitch in self._active_pitches)
            km = self.mapper.map_pitch(pitch)

            if is_active:
                brush = QBrush(QColor("#F59E0B"))  # Active Amber
                pen = QPen(QColor("#FBBF24"), 1)
                text_color = QColor("#000000")
            else:
                brush = QBrush(QColor("#1E2128"))  # Deep Black/Charcoal
                pen = QPen(QColor("#353A45"), 1)
                text_color = QColor("#E2E6EE")

            painter.setBrush(brush)
            painter.setPen(pen)
            painter.drawRoundedRect(rect, 2, 2)

            # Draw Roblox Key label (Shifted character)
            if km:
                painter.setPen(text_color)
                painter.setFont(font_label)
                painter.drawText(
                    QRectF(rect.x(), rect.bottom() - 20, rect.width(), 14),
                    Qt.AlignCenter,
                    km.char
                )
