"""
Roblox Piano Player - Floating Game HUD Overlay
"""
import ctypes
from typing import Optional
from PySide6.QtCore import Qt, QPoint, Signal
from PySide6.QtGui import QPainter, QColor, QFont, QPen, QBrush, QMouseEvent
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QProgressBar, QFrame
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
        self.resize(360, 112)

    def _setup_ui(self) -> None:
        self.setStyleSheet("""
            QWidget {
                background: transparent;
                color: #F3F4F6;
                font-family: "Malgun Gothic", "Segoe UI", sans-serif;
            }
            QFrame#overlayCard {
                background: rgba(18, 19, 22, 238);
                border: 1px solid rgba(255, 255, 255, 34);
                border-radius: 8px;
            }
            QFrame#overlayCard[state="playing"] { border-color: #10B981; }
            QFrame#overlayCard[state="countdown"] { border-color: #F59E0B; }
            QLabel#overlayTitle { color: #F3F4F6; font-size: 12px; font-weight: 600; }
            QLabel#overlayStatus {
                padding: 2px 6px;
                border-radius: 4px;
                background: #22252C;
                color: #9CA3AF;
                font-size: 10px;
                font-weight: 600;
            }
            QLabel#overlayStatus[state="playing"] { color: #10B981; }
            QLabel#overlayStatus[state="countdown"] { color: #F59E0B; }
            QLabel#overlayStatus[state="paused"] { color: #F59E0B; }
            QLabel#overlayCountdown { color: #F59E0B; font-size: 18px; font-weight: 700; }
            QLabel#overlayInfo {
                color: #9CA3AF;
                font-size: 10px;
                font-family: "Cascadia Mono", "Consolas", monospace;
            }
            QLabel#overlayHands { color: #14B8A6; font-size: 10px; }
            QLabel#overlayHint { color: #6B7280; font-size: 9px; }
            QPushButton#overlayIcon {
                min-width: 22px; max-width: 22px; min-height: 22px; max-height: 22px;
                padding: 0; background: transparent; border: none; color: #9CA3AF;
            }
            QPushButton#overlayPrimary {
                min-height: 25px; padding: 0 9px;
                background: #3B82F6; border: none; border-radius: 5px;
                color: white; font-size: 10px; font-weight: 600;
            }
            QPushButton#overlaySecondary {
                min-height: 25px; padding: 0 8px;
                background: #22252C; border: 1px solid #262932; border-radius: 5px;
                color: #C9CBD0; font-size: 10px; font-weight: 600;
            }
            QProgressBar {
                min-height: 4px; max-height: 4px;
                background: rgba(255,255,255,28); border: none; border-radius: 2px;
            }
            QProgressBar::chunk { background: #3B82F6; border-radius: 2px; }
        """)

        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(8, 8, 8, 8)
        self.main_layout.setSpacing(0)

        self.card = QFrame(self)
        self.card.setObjectName("overlayCard")
        self.card_layout = QVBoxLayout(self.card)
        self.card_layout.setContentsMargins(12, 8, 12, 8)
        self.card_layout.setSpacing(5)

        self.header_layout = QHBoxLayout()
        self.header_layout.setContentsMargins(0, 0, 0, 0)
        self.header_layout.setSpacing(7)
        self.lbl_title = QLabel(self._song_title)
        self.lbl_title.setObjectName("overlayTitle")
        self.lbl_status = QLabel("준비됨")
        self.lbl_status.setObjectName("overlayStatus")
        self.lbl_status.setProperty("state", "ready")
        self.btn_toggle_mode = QPushButton("—")
        self.btn_toggle_mode.setObjectName("overlayIcon")
        self.btn_toggle_mode.clicked.connect(self.toggle_compact_mode)
        self.header_layout.addWidget(self.lbl_title, 1)
        self.header_layout.addWidget(self.lbl_status)
        self.header_layout.addWidget(self.btn_toggle_mode)
        self.card_layout.addLayout(self.header_layout)

        self.lbl_countdown = QLabel("")
        self.lbl_countdown.setObjectName("overlayCountdown")
        self.lbl_countdown.setAlignment(Qt.AlignCenter)
        self.lbl_countdown.hide()
        self.card_layout.addWidget(self.lbl_countdown)

        self.progress_bar = QProgressBar()
        self.progress_bar.setFixedHeight(4)
        self.progress_bar.setTextVisible(False)
        self.card_layout.addWidget(self.progress_bar)

        self.sub_layout = QHBoxLayout()
        self.sub_layout.setContentsMargins(0, 0, 0, 0)
        self.sub_layout.setSpacing(6)
        self.lbl_time = QLabel("00:00 / 00:00")
        self.lbl_time.setObjectName("overlayInfo")
        self.lbl_hands = QLabel("오른손 ●  왼손 ●")
        self.lbl_hands.setObjectName("overlayHands")
        self.lbl_speed = QLabel("1.00×")
        self.lbl_speed.setObjectName("overlayInfo")
        self.sub_layout.addWidget(self.lbl_time)
        self.sub_layout.addStretch()
        self.sub_layout.addWidget(self.lbl_hands)
        self.sub_layout.addStretch()
        self.sub_layout.addWidget(self.lbl_speed)
        self.card_layout.addLayout(self.sub_layout)

        self.controls_layout = QHBoxLayout()
        self.controls_layout.setContentsMargins(0, 0, 0, 0)
        self.controls_layout.setSpacing(5)
        self.lbl_hotkeys_hint = QLabel("F6 재생  ·  F7 일시 정지  ·  F8 정지")
        self.lbl_hotkeys_hint.setObjectName("overlayHint")
        self.btn_play = QPushButton("재생  F6")
        self.btn_play.setObjectName("overlayPrimary")
        self.btn_play.clicked.connect(self.play_requested.emit)
        self.btn_stop = QPushButton("정지  F8")
        self.btn_stop.setObjectName("overlaySecondary")
        self.btn_stop.clicked.connect(self.stop_requested.emit)
        self.controls_layout.addWidget(self.lbl_hotkeys_hint, 1)
        self.controls_layout.addWidget(self.btn_play)
        self.controls_layout.addWidget(self.btn_stop)
        self.card_layout.addLayout(self.controls_layout)

        self.main_layout.addWidget(self.card)
        self.resize(360, 112)

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
        state_names = {
            PlaybackState.IDLE: ("준비됨", "ready"),
            PlaybackState.COUNTDOWN: ("시작 대기", "countdown"),
            PlaybackState.PLAYING: ("연주 중", "playing"),
            PlaybackState.PAUSED: ("일시 정지", "paused"),
            PlaybackState.STOPPED: ("정지됨", "ready"),
            PlaybackState.COMPLETED: ("완료됨", "ready"),
        }
        text, state_name = state_names.get(state, ("준비됨", "ready"))
        self.lbl_status.setText(text)
        self.lbl_status.setProperty("state", state_name)
        self.card.setProperty("state", state_name)
        self.lbl_status.style().unpolish(self.lbl_status)
        self.lbl_status.style().polish(self.lbl_status)
        self.card.style().unpolish(self.card)
        self.card.style().polish(self.card)

        if state == PlaybackState.PLAYING:
            self.btn_play.setText("일시 정지  F7")
            self.lbl_countdown.hide()
        elif state == PlaybackState.PAUSED:
            self.btn_play.setText("계속  F7")
            self.lbl_countdown.hide()
        else:
            self.btn_play.setText("재생  F6")

    def toggle_compact_mode(self) -> None:
        self._is_compact = not self._is_compact
        if self._is_compact:
            self.progress_bar.hide()
            self.sub_layout.itemAt(0).widget().hide()  # lbl_time
            self.sub_layout.itemAt(2).widget().hide()  # lbl_hands
            self.sub_layout.itemAt(4).widget().hide()  # lbl_speed
            self.controls_layout.itemAt(0).widget().hide()  # hint
            self.btn_toggle_mode.setText("+")
            self.resize(320, 68)
        else:
            self.progress_bar.show()
            self.sub_layout.itemAt(0).widget().show()
            self.sub_layout.itemAt(2).widget().show()
            self.sub_layout.itemAt(4).widget().show()
            self.controls_layout.itemAt(0).widget().show()
            self.btn_toggle_mode.setText("—")
            self.resize(360, 112)

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
