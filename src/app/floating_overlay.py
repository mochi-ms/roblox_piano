"""
Roblox Piano Player - Floating Game HUD Overlay
"""
import ctypes
from typing import Optional
from PySide6.QtCore import Qt, QPoint, Signal
from PySide6.QtGui import QPainter, QColor, QFont, QPen, QBrush, QMouseEvent
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QProgressBar, QGraphicsDropShadowEffect, QFrame
)
from src.playback.scheduler import PlaybackState


class FloatingOverlay(QWidget):
    """
    Always-on-top, draggable, frameless HUD overlay for Roblox gameplay.
    Supports Compact and Expanded modes, animated countdown, and click-through.
    """
    play_requested = Signal()
    pause_requested = Signal()
    stop_requested = Signal()
    mode_toggled = Signal(bool)  # True if compact

    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent, Qt.WindowStaysOnTopHint | Qt.FramelessWindowHint | Qt.Tool)
        self.setAttribute(Qt.WA_TranslucentBackground, True)

        self._is_compact: bool = False
        self._is_dragging: bool = False
        self._drag_position = QPoint()
        self._click_through: bool = False

        self._song_title: str = "곡 없음"
        self._state: PlaybackState = PlaybackState.IDLE
        self._current_time: float = 0.0
        self._total_time: float = 0.0
        self._speed: float = 1.0
        self._countdown: int = 0
        self._rh_active: bool = True
        self._lh_active: bool = True

        self._setup_ui()
        self.resize(340, 160)

    def _setup_ui(self) -> None:
        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(10, 10, 10, 10)

        # Card container
        self.card = QFrame(self)
        self.card.setObjectName("overlay_card")
        self.card.setStyleSheet("""
            QFrame#overlay_card {
                background-color: rgba(20, 23, 29, 230);
                border: 1px solid rgba(255, 255, 255, 0.12);
                border-radius: 12px;
            }
        """)

        # Drop shadow
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(20)
        shadow.setColor(QColor(0, 0, 0, 160))
        shadow.setOffset(0, 4)
        self.card.setGraphicsEffect(shadow)

        self.card_layout = QVBoxLayout(self.card)
        self.card_layout.setContentsMargins(14, 12, 14, 12)
        self.card_layout.setSpacing(8)

        # Header: Title + Status + Compact toggle
        self.header_layout = QHBoxLayout()
        self.lbl_title = QLabel(self._song_title)
        self.lbl_title.setStyleSheet("font-weight: bold; font-size: 13px; color: #FFFFFF;")

        self.lbl_status = QLabel("준비됨")
        self.lbl_status.setStyleSheet("font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 4px; background-color: #2D3748; color: #E2E8F0;")

        self.btn_toggle_mode = QPushButton("—")
        self.btn_toggle_mode.setFixedSize(22, 22)
        self.btn_toggle_mode.setStyleSheet("background-color: transparent; border: none; color: #A0AEC0; font-size: 14px;")
        self.btn_toggle_mode.clicked.connect(self.toggle_compact_mode)

        self.header_layout.addWidget(self.lbl_title, 1)
        self.header_layout.addWidget(self.lbl_status)
        self.header_layout.addWidget(self.btn_toggle_mode)
        self.card_layout.addLayout(self.header_layout)

        # Countdown label (prominent display)
        self.lbl_countdown = QLabel("")
        self.lbl_countdown.setAlignment(Qt.AlignCenter)
        self.lbl_countdown.setStyleSheet("font-size: 26px; font-weight: 900; color: #F59E0B; margin: 4px 0px;")
        self.lbl_countdown.hide()
        self.card_layout.addWidget(self.lbl_countdown)

        # Progress bar
        self.progress_bar = QProgressBar()
        self.progress_bar.setFixedHeight(4)
        self.progress_bar.setTextVisible(False)
        self.progress_bar.setStyleSheet("""
            QProgressBar { background-color: rgba(255, 255, 255, 0.1); border-radius: 2px; }
            QProgressBar::chunk { background-color: #3875E8; border-radius: 2px; }
        """)
        self.card_layout.addWidget(self.progress_bar)

        # Sub-info: Time + Hands + Speed
        self.sub_layout = QHBoxLayout()
        self.lbl_time = QLabel("00:00 / 00:00")
        self.lbl_time.setStyleSheet("font-size: 11px; color: #94A3B8; font-family: monospace;")

        self.lbl_hands = QLabel("오른손 ●  왼손 ●")
        self.lbl_hands.setStyleSheet("font-size: 11px; color: #38BDF8;")

        self.lbl_speed = QLabel("1.00×")
        self.lbl_speed.setStyleSheet("font-size: 11px; color: #94A3B8;")

        self.sub_layout.addWidget(self.lbl_time)
        self.sub_layout.addStretch()
        self.sub_layout.addWidget(self.lbl_hands)
        self.sub_layout.addStretch()
        self.sub_layout.addWidget(self.lbl_speed)
        self.card_layout.addLayout(self.sub_layout)

        # Controls: F6 Play/Pause, F8 Stop
        self.controls_layout = QHBoxLayout()
        self.lbl_hotkeys_hint = QLabel("F6: 재생/일시정지  •  F8: 정지")
        self.lbl_hotkeys_hint.setStyleSheet("font-size: 10px; color: #64748B;")

        self.btn_play = QPushButton("▶ F6")
        self.btn_play.setFixedHeight(26)
        self.btn_play.setStyleSheet("background-color: #2563EB; border: none; border-radius: 4px; color: white; font-weight: bold; font-size: 11px; padding: 0 10px;")
        self.btn_play.clicked.connect(self.play_requested.emit)

        self.btn_stop = QPushButton("■ F8")
        self.btn_stop.setFixedHeight(26)
        self.btn_stop.setStyleSheet("background-color: #334155; border: none; border-radius: 4px; color: #CBD5E1; font-weight: bold; font-size: 11px; padding: 0 8px;")
        self.btn_stop.clicked.connect(self.stop_requested.emit)

        self.controls_layout.addWidget(self.lbl_hotkeys_hint, 1)
        self.controls_layout.addWidget(self.btn_play)
        self.controls_layout.addWidget(self.btn_stop)
        self.card_layout.addLayout(self.controls_layout)

        self.main_layout.addWidget(self.card)

    def set_song_title(self, title: str) -> None:
        self._song_title = title
        short_title = title if len(title) <= 24 else title[:22] + "..."
        self.lbl_title.setText(short_title)

    def set_hands(self, rh: bool, lh: bool) -> None:
        self._rh_active = rh
        self._lh_active = lh
        rh_str = "오른손 ●" if rh else "오른손 ○"
        lh_str = "왼손 ●" if lh else "왼손 ○"
        self.lbl_hands.setText(f"{rh_str}  {lh_str}")

    def set_speed(self, speed: float) -> None:
        self._speed = speed
        self.lbl_speed.setText(f"{speed:.2f}×")

    def set_countdown(self, sec: int) -> None:
        self._countdown = sec
        if sec > 0:
            self.lbl_countdown.setText(f"{sec}초 후 시작")
            self.lbl_countdown.show()
        else:
            self.lbl_countdown.hide()

    def set_progress(self, current: float, total: float) -> None:
        self._current_time = current
        self._total_time = total

        c_min, c_sec = divmod(int(current), 60)
        t_min, t_sec = divmod(int(total), 60)
        self.lbl_time.setText(f"{c_min:02d}:{c_sec:02d} / {t_min:02d}:{t_sec:02d}")

        if total > 0:
            pct = int((current / total) * 100)
            self.progress_bar.setValue(pct)
        else:
            self.progress_bar.setValue(0)

    def set_playback_state(self, state: PlaybackState) -> None:
        self._state = state
        state_colors = {
            PlaybackState.IDLE: ("준비됨", "#2D3748", "#E2E8F0"),
            PlaybackState.COUNTDOWN: ("시작 대기", "#D97706", "#FEF3C7"),
            PlaybackState.PLAYING: ("연주 중", "#059669", "#D1FAE5"),
            PlaybackState.PAUSED: ("일시정지됨", "#DC2626", "#FEE2E2"),
            PlaybackState.STOPPED: ("정지됨", "#475569", "#F1F5F9"),
            PlaybackState.COMPLETED: ("완료됨", "#2563EB", "#DBEAFE"),
        }
        text, bg, fg = state_colors.get(state, ("준비됨", "#2D3748", "#E2E8F0"))
        self.lbl_status.setText(text)
        self.lbl_status.setStyleSheet(f"font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 4px; background-color: {bg}; color: {fg};")

        if state == PlaybackState.PLAYING:
            self.btn_play.setText("⏸ F7")
            self.lbl_countdown.hide()
        elif state == PlaybackState.PAUSED:
            self.btn_play.setText("▶ F7")
            self.lbl_countdown.hide()
        else:
            self.btn_play.setText("▶ F6")

    def toggle_compact_mode(self) -> None:
        self._is_compact = not self._is_compact
        if self._is_compact:
            self.progress_bar.hide()
            self.sub_layout.itemAt(0).widget().hide()  # lbl_time
            self.sub_layout.itemAt(2).widget().hide()  # lbl_hands
            self.sub_layout.itemAt(4).widget().hide()  # lbl_speed
            self.controls_layout.itemAt(0).widget().hide()  # hint
            self.btn_toggle_mode.setText("+")
            self.resize(260, 68)
        else:
            self.progress_bar.show()
            self.sub_layout.itemAt(0).widget().show()
            self.sub_layout.itemAt(2).widget().show()
            self.sub_layout.itemAt(4).widget().show()
            self.controls_layout.itemAt(0).widget().show()
            self.btn_toggle_mode.setText("—")
            self.resize(340, 160)

        self.mode_toggled.emit(self._is_compact)

    def set_click_through(self, enabled: bool) -> None:
        """Sets Windows WS_EX_TRANSPARENT style for game click-through."""
        self._click_through = enabled
        hwnd = int(self.winId())
        GWL_EXSTYLE = -20
        WS_EX_TRANSPARENT = 0x00000020
        WS_EX_LAYERED = 0x00080000

        user32 = ctypes.windll.user32
        style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        if enabled:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED)
        else:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT)

    # Mouse Dragging Support
    def mousePressEvent(self, event: QMouseEvent) -> None:
        if event.button() == Qt.LeftButton and not self._click_through:
            self._is_dragging = True
            self._drag_position = event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            event.accept()

    def mouseMoveEvent(self, event: QMouseEvent) -> None:
        if self._is_dragging and event.buttons() == Qt.LeftButton and not self._click_through:
            self.move(event.globalPosition().toPoint() - self._drag_position)
            event.accept()

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        self._is_dragging = False
