"""
Roblox Piano Player - Piano Roll Visualization Widget
"""
from typing import Optional, List, Callable
from PySide6.QtCore import Qt, QRectF, Signal
from PySide6.QtGui import QPainter, QColor, QPen, QBrush, QFont, QMouseEvent
from PySide6.QtWidgets import QWidget
from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline


class PianoRollWidget(QWidget):
    """
    Renders note events on an interactive piano roll with distinct RH/LH colors and a playhead.
    Supports clicking/dragging to seek.
    """
    seek_requested = Signal(float)

    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._timeline: Optional[MusicTimeline] = None
        self._current_time: float = 0.0
        self._min_pitch: int = 21  # A0
        self._max_pitch: int = 108  # C8
        self.setMinimumHeight(140)
        self.setMouseTracking(True)
        self._is_dragging: bool = False

    def set_timeline(self, timeline: Optional[MusicTimeline]) -> None:
        self._timeline = timeline
        self._current_time = 0.0
        if timeline and timeline.notes:
            pitches = [n.pitch for n in timeline.notes]
            self._min_pitch = min(21, min(pitches))
            self._max_pitch = max(108, max(pitches))
        else:
            self._min_pitch = 21
            self._max_pitch = 108
        self.update()

    def set_playhead_time(self, current_time: float) -> None:
        self._current_time = current_time
        self.update()

    def mousePressEvent(self, event: QMouseEvent) -> None:
        if event.button() == Qt.LeftButton and self._timeline and self._timeline.duration > 0:
            self._is_dragging = True
            self._handle_seek_from_pos(event.position().x())

    def mouseMoveEvent(self, event: QMouseEvent) -> None:
        if self._is_dragging and self._timeline and self._timeline.duration > 0:
            self._handle_seek_from_pos(event.position().x())

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        if event.button() == Qt.LeftButton:
            self._is_dragging = False

    def _handle_seek_from_pos(self, x: float) -> None:
        w = self.width() - 40
        if w <= 0:
            return
        ratio = max(0.0, min(1.0, (x - 20) / w))
        target_time = ratio * self._timeline.duration
        self.seek_requested.emit(target_time)

    def paintEvent(self, event) -> None:
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing, True)

        w = self.width()
        h = self.height()

        # Background
        painter.fillRect(0, 0, w, h, QColor("#15181E"))

        # Border
        painter.setPen(QPen(QColor("#252A34"), 1))
        painter.drawRoundedRect(1, 1, w - 2, h - 2, 8, 8)

        if not self._timeline or not self._timeline.notes or self._timeline.duration <= 0:
            painter.setPen(QColor("#5A6272"))
            painter.setFont(QFont("Segoe UI", 11))
            painter.drawText(self.rect(), Qt.AlignCenter, "불러온 악보가 없습니다")
            return

        margin_x = 20
        margin_y = 10
        draw_w = w - (margin_x * 2)
        draw_h = h - (margin_y * 2)

        duration = max(1.0, self._timeline.duration)
        pitch_span = max(1, self._max_pitch - self._min_pitch)

        # Draw grid lines for octaves
        painter.setPen(QPen(QColor("#1F232B"), 1))
        for p in range(self._min_pitch, self._max_pitch + 1):
            if p % 12 == 0:  # C note
                y = margin_y + draw_h - ((p - self._min_pitch) / pitch_span * draw_h)
                painter.drawLine(margin_x, int(y), int(w - margin_x), int(y))

        # Draw notes
        for note in self._timeline.notes:
            # Calculate X and width
            x1 = margin_x + (note.start_time / duration * draw_w)
            x2 = margin_x + (note.end_time / duration * draw_w)
            note_w = max(3.0, x2 - x1)

            # Calculate Y and height
            pitch_norm = (note.pitch - self._min_pitch) / pitch_span
            y = margin_y + draw_h - (pitch_norm * draw_h)
            note_h = max(2.5, draw_h / (pitch_span + 1) * 1.5)

            # Colors by hand
            if note.hand == HandType.RIGHT:
                color = QColor("#3B82F6")  # Vibrant Blue
            elif note.hand == HandType.LEFT:
                color = QColor("#8B5CF6")  # Vibrant Purple
            else:
                color = QColor("#10B981")  # Emerald Green

            # Out of Roblox range highlight
            if not (21 <= note.pitch <= 108):
                color = QColor("#EF4444")  # Red for out-of-range

            painter.setPen(Qt.NoPen)
            painter.setBrush(QBrush(color))
            painter.drawRoundedRect(QRectF(x1, y - note_h / 2, note_w, note_h), 2, 2)

        # Draw Playhead
        playhead_x = margin_x + (self._current_time / duration * draw_w)
        painter.setPen(QPen(QColor("#F59E0B"), 2))  # Amber playhead
        painter.drawLine(int(playhead_x), margin_y, int(playhead_x), int(h - margin_y))

        # Playhead triangle head
        painter.setBrush(QBrush(QColor("#F59E0B")))
        painter.setPen(Qt.NoPen)
        painter.drawPolygon([
            (int(playhead_x - 5), margin_y),
            (int(playhead_x + 5), margin_y),
            (int(playhead_x), margin_y + 7)
        ])
