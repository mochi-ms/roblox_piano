"""
Roblox Piano Player - Main Application Window
"""
import os
from collections import deque
from typing import Optional, List, Dict
from PySide6.QtCore import Qt, QTimer, Signal, Slot, QThread
from PySide6.QtGui import QDragEnterEvent, QDropEvent, QIcon, QFont
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QStackedWidget, QFileDialog, QSlider, QFrame, QMessageBox, QDialog,
    QScrollArea, QCheckBox, QTabWidget, QButtonGroup, QProgressBar,
    QStyle, QSizePolicy
)

from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline
from src.music.transpose import Transposer
from src.music.range_processor import RangeProcessor, RangeAnalysisResult
from src.piano.mapper import RobloxPianoMapper
from src.importers.numeric_importer import NumericImporter
from src.playback.keyboard_backend import KeyboardBackend
from src.playback.sendinput_backend import SendInputBackend
from src.playback.dryrun_backend import DryRunBackend
from src.playback.key_state_manager import KeyStateManager
from src.playback.chord_engine import ChordEngine, ConflictPolicy
from src.playback.scheduler import PlaybackScheduler, PlaybackState
from src.playback.pedal_backend import MousePedalBackend
from src.hotkeys.global_hotkeys import GlobalHotkeyManager
from src.windows.target_window import TargetWindowManager, FocusLossPolicy
from src.utils.config import AppConfig, ConfigManager
from src.utils.logger import logger
from src.app.theme import get_stylesheet
from src.app.piano_roll_widget import PianoRollWidget
from src.app.virtual_piano_widget import VirtualPianoWidget
from src.app.floating_overlay import FloatingOverlay
from src.app.settings_window import SettingsDialog
from src.library.manager import LibraryManager
from src.app.library_widget import LibraryWidget
from src.app.import_widget import ImportWidget
from src.video.pipeline import VideoAnalysisWorker
from src.library.models import ScoreItem


class ScoreLoadWorker(QThread):
    """Parse score files off the GUI thread so large imports cannot freeze the window."""

    def __init__(self, file_path: str, parent=None):
        super().__init__(parent)
        self.file_path = file_path
        self.timeline: Optional[MusicTimeline] = None
        self.error = ""

    def run(self) -> None:
        try:
            ext = os.path.splitext(self.file_path)[1].lower()
            if ext in (".mid", ".midi"):
                from src.importers.midi_importer import MidiImporter
                importer = MidiImporter()
            elif ext in (".musicxml", ".xml", ".mxl"):
                from src.importers.musicxml_importer import MusicXmlImporter
                importer = MusicXmlImporter()
            elif ext in (".txt", ".num", ".jianpu"):
                importer = NumericImporter()
            elif ext in (".png", ".jpg", ".jpeg", ".bmp", ".tiff"):
                from src.importers.image_importer import ImageImporter
                importer = ImageImporter()
            elif ext == ".pdf":
                from src.importers.pdf_importer import PdfImporter
                importer = PdfImporter()
            else:
                raise ValueError(f"지원하지 않는 파일 확장자입니다: {ext}")
            self.timeline = importer.import_score(self.file_path)
            if self.timeline is None:
                raise RuntimeError("악보를 읽었지만 연주할 음표를 찾지 못했습니다.")
            if self.isInterruptionRequested():
                self.timeline = None
                self.error = "불러오기가 취소되었습니다."
        except Exception as exc:
            self.error = str(exc)


class ElidedLabel(QLabel):
    def __init__(self, text: str = "", parent=None):
        super().__init__(text, parent)
        self._full_text = text
        self.setToolTip(text)

    def set_elided_text(self, text: str) -> None:
        self._full_text = text
        self.setToolTip(text)
        self._apply_elision()

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        self._apply_elision()

    def _apply_elision(self) -> None:
        width = max(0, self.contentsRect().width())
        if width:
            display = self.fontMetrics().elidedText(
                self._full_text, Qt.ElideRight, width
            )
        else:
            display = self._full_text
        QLabel.setText(self, display)


def _pitch_label(pitch: int) -> str:
    names = ("C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B")
    return f"{names[pitch % 12]}{pitch // 12 - 1}"


class MainWindow(QMainWindow):
    # Cross-thread UI update signals for high safety
    sig_state_changed = Signal(str)
    sig_progress = Signal(float, float)
    sig_countdown = Signal(int)
    sig_notes_played = Signal(list)

    def __init__(self):
        super().__init__()
        self.setWindowTitle("로블록스 자동 피아노 연주기")
        self.resize(860, 720)
        self.setMinimumSize(780, 620)
        self.setAcceptDrops(True)

        # Set window icon
        from src.utils.icon_loader import get_app_qicon
        app_icon = get_app_qicon()
        if not app_icon.isNull():
            self.setWindowIcon(app_icon)

        # 1. Config & Core Services
        self.config_mgr = ConfigManager()
        self.config: AppConfig = self.config_mgr.config
        self.mapper = RobloxPianoMapper()

        # Target Window Manager (Must be created before Playback Stack so scheduler can use it)
        self.target_window = TargetWindowManager(
            policy=FocusLossPolicy(self.config.focus_loss_policy),
            enabled=self.config.target_window_safety
        )

        # Keyboard & Playback Stack
        self._init_playback_stack()

        self.numeric_importer = NumericImporter()

        # Library
        self.library_manager = LibraryManager(self.config_mgr)
        self.active_worker: Optional[VideoAnalysisWorker] = None
        self._score_load_queue = deque()
        self._score_load_worker: Optional[ScoreLoadWorker] = None
        self._closing_after_workers = False
        self._last_roblox_foreground: Optional[bool] = None

        # State
        self.timeline: Optional[MusicTimeline] = None
        self._active_notes_timer = QTimer(self)
        self._active_notes_timer.setInterval(100)
        self._active_notes_timer.timeout.connect(self._clear_active_piano_keys)

        # Hotkeys
        self.hotkeys = GlobalHotkeyManager()

        # 2. UI Setup
        self._setup_ui()
        self.import_widget.btn_cancel.clicked.connect(self._cancel_active_video_import)
        self._setup_overlay()
        self._connect_signals()
        self._apply_theme()
        self._register_hotkeys()

        # Timer for polling Roblox window focus status
        self._focus_poll_timer = QTimer(self)
        self._focus_poll_timer.setInterval(1000)
        self._focus_poll_timer.timeout.connect(self._update_roblox_focus_status)
        self._focus_poll_timer.start()

    def _init_playback_stack(self) -> None:
        if self.config.dry_run_mode:
            self.backend: KeyboardBackend = DryRunBackend()
            logger.log("Initialized DryRun Backend (keystrokes will be simulated in memory).")
        else:
            self.backend: KeyboardBackend = SendInputBackend()
            logger.log("Initialized Win32 SendInput Backend.")

        self.key_state = KeyStateManager(self.backend)
        self.chord_engine = ChordEngine(
            key_state=self.key_state,
            mapper=self.mapper,
            conflict_policy=ConflictPolicy(self.config.conflict_policy),
            conflict_delay_ms=self.config.conflict_delay_ms,
            default_hold_duration_ms=self.config.hold_duration_ms,
            on_log=logger.log
        )

        self.pedal_backend = MousePedalBackend()
        self.pedal_backend.setup(
            enabled=self.config.pedal_enabled,
            x_ratio=self.config.pedal_x_ratio,
            y_ratio=self.config.pedal_y_ratio,
            interaction_mode=self.config.pedal_mode,
            restore_cursor=True
        )

        self.scheduler = PlaybackScheduler(
            chord_engine=self.chord_engine,
            key_state=self.key_state
        )
        self.scheduler.pedal_backend = self.pedal_backend
        self.scheduler.target_hwnd_getter = self.target_window.get_roblox_hwnd
        self.scheduler.speed = self.config.playback_speed
        self.scheduler.countdown_seconds = self.config.countdown_seconds
        self.scheduler.enable_rh = self.config.enable_rh
        self.scheduler.enable_lh = self.config.enable_lh
        self.scheduler.on_log = logger.log

        # Set callbacks to emit Qt signals (thread-safe UI update)
        self.scheduler.on_state_changed = lambda st: self.sig_state_changed.emit(st.value)
        self.scheduler.on_progress = lambda cur, tot: self.sig_progress.emit(cur, tot)
        self.scheduler.on_countdown = lambda sec: self.sig_countdown.emit(sec)
        self.scheduler.on_chord_played = lambda notes: self.sig_notes_played.emit(notes)

    def _setup_ui(self) -> None:
        central_widget = QWidget(self)
        self.setCentralWidget(central_widget)
        self.root_layout = QVBoxLayout(central_widget)
        self.root_layout.setContentsMargins(12, 10, 12, 2)
        self.root_layout.setSpacing(8)

        self.header = QFrame(central_widget)
        self.header.setObjectName("GlobalHeader")
        self.header.setFixedHeight(48)
        header_layout = QHBoxLayout(self.header)
        header_layout.setContentsMargins(8, 0, 8, 0)
        header_layout.setSpacing(6)

        self.lbl_app_title = QLabel("로블록스 피아노")
        self.lbl_app_title.setObjectName("appTitle")
        self.lbl_status_badge = QLabel("준비됨")
        self.lbl_status_badge.setObjectName("statusBadge")
        self.lbl_status_badge.setProperty("state", "ready")

        self.workspace_nav = QWidget(self.header)
        nav_layout = QHBoxLayout(self.workspace_nav)
        nav_layout.setContentsMargins(0, 0, 0, 0)
        nav_layout.setSpacing(2)
        self.workspace_nav_group = QButtonGroup(self)
        self.workspace_nav_group.setExclusive(True)
        self.workspace_nav_buttons = []
        for index, title in enumerate(("연주", "라이브러리", "가져오기")):
            button = QPushButton(title, self.workspace_nav)
            button.setObjectName("workspaceSegment")
            button.setCheckable(True)
            self.workspace_nav_group.addButton(button, index)
            button.clicked.connect(
                lambda checked=False, tab_index=index: self.tabs.setCurrentIndex(tab_index)
                if hasattr(self, "tabs") else None
            )
            nav_layout.addWidget(button)
            self.workspace_nav_buttons.append(button)

        self.lbl_roblox_status = QLabel("○ Roblox 대기")
        self.lbl_roblox_status.setObjectName("robloxStatus")
        self.lbl_roblox_status.setProperty("state", "waiting")
        self.lbl_roblox_status.setToolTip("연주를 시작하면 Roblox 창을 자동으로 확인합니다")

        self.btn_overlay_toggle = QPushButton("오버레이  F4")
        self.btn_overlay_toggle.setObjectName("headerToggle")
        self.btn_overlay_toggle.setCheckable(True)
        self.btn_overlay_toggle.setChecked(False)
        self.btn_overlay_toggle.clicked.connect(self._toggle_overlay)

        self.btn_settings = QPushButton("설정")
        self.btn_settings.setObjectName("headerAction")
        self.btn_settings.clicked.connect(self._open_settings)

        header_layout.addWidget(self.lbl_app_title)
        header_layout.addWidget(self.lbl_status_badge)
        header_layout.addStretch(1)
        header_layout.addWidget(self.workspace_nav)
        header_layout.addStretch(1)
        header_layout.addWidget(self.lbl_roblox_status)
        header_layout.addWidget(self.btn_overlay_toggle)
        header_layout.addWidget(self.btn_settings)
        self.root_layout.addWidget(self.header)

        self.tabs = QTabWidget(self)
        self.tabs.setObjectName("workspaceStack")
        self.tabs.tabBar().hide()
        self.tabs.setDocumentMode(True)
        self.tabs.currentChanged.connect(self._sync_workspace_nav)
        self.root_layout.addWidget(self.tabs, 1)

        self.player_container = QWidget()
        player_layout = QVBoxLayout(self.player_container)
        player_layout.setContentsMargins(0, 0, 0, 0)
        self.view_stack = QStackedWidget()
        player_layout.addWidget(self.view_stack)
        self._build_landing_view()
        self._build_player_view()
        self.tabs.addTab(self.player_container, "연주")

        self.library_widget = LibraryWidget(self.library_manager, self)
        self.library_widget.score_selected.connect(self._on_library_score_selected)
        self.tabs.addTab(self.library_widget, "라이브러리")

        self.import_widget = ImportWidget(self)
        self.import_widget.import_requested.connect(self._on_import_requested)
        self.import_widget.score_open_requested.connect(self._on_library_score_selected)
        self.tabs.addTab(self.import_widget, "가져오기")
        for button in self.workspace_nav_buttons:
            button.setChecked(False)
        self.workspace_nav_buttons[0].setChecked(True)

        status = self.statusBar()
        status.setSizeGripEnabled(False)
        status.setFixedHeight(28)
        self.lbl_shortcut_hint = QLabel("F6 재생  ·  F7 일시 정지/계속  ·  F8 정지  ·  F4 오버레이")
        status.addWidget(self.lbl_shortcut_hint, 1)
        self.lbl_background_task = QLabel("동영상 분석")
        self.lbl_background_task.setObjectName("statusBadge")
        self.lbl_background_task.setProperty("state", "countdown")
        self.lbl_background_task.setContentsMargins(6, 0, 6, 0)
        self.background_task_progress = QProgressBar()
        self.background_task_progress.setTextVisible(False)
        self.background_task_progress.setFixedWidth(100)
        status.addPermanentWidget(self.lbl_background_task)
        status.addPermanentWidget(self.background_task_progress)
        self.lbl_background_task.hide()
        self.background_task_progress.hide()

    def _sync_workspace_nav(self, index: int) -> None:
        for tab_index, button in enumerate(self.workspace_nav_buttons):
            button.setChecked(tab_index == index)

    def _build_landing_view(self) -> None:
        self.landing_view = QWidget()
        layout = QVBoxLayout(self.landing_view)
        layout.setContentsMargins(8, 4, 8, 4)
        layout.setAlignment(Qt.AlignCenter)

        self.drop_card = QFrame(self.landing_view)
        self.drop_card.setObjectName("drop_card")
        self.drop_card.setMinimumSize(0, 300)
        self.drop_card.setMaximumWidth(650)
        drop_layout = QVBoxLayout(self.drop_card)
        drop_layout.setContentsMargins(24, 22, 24, 22)
        drop_layout.setAlignment(Qt.AlignCenter)
        drop_layout.setSpacing(12)

        lbl_icon = QLabel(self.drop_card)
        lbl_icon.setAlignment(Qt.AlignCenter)
        lbl_icon.setPixmap(self.style().standardIcon(QStyle.SP_FileIcon).pixmap(38, 38))

        lbl_main = QLabel("악보 파일을 이곳에 끌어다 놓으세요", self.drop_card)
        lbl_main.setObjectName("dropTitle")
        lbl_main.setAlignment(Qt.AlignCenter)

        lbl_formats = QLabel(
            "MIDI   ·   MusicXML   ·   숫자 악보   ·   PDF   ·   이미지",
            self.drop_card,
        )
        lbl_formats.setObjectName("dropHint")
        lbl_formats.setAlignment(Qt.AlignCenter)

        self.btn_browse_score = QPushButton("내 PC에서 파일 찾기")
        self.btn_browse_score.setObjectName("primary_btn")
        self.btn_browse_score.setMinimumWidth(170)
        self.btn_browse_score.clicked.connect(self._browse_file)

        self.btn_demo_score = QPushButton("데모 곡 불러오기")
        self.btn_demo_score.setObjectName("secondaryAction")
        self.btn_demo_score.setMinimumWidth(150)
        self.btn_demo_score.clicked.connect(self._load_sample_score)

        actions = QHBoxLayout()
        actions.setSpacing(8)
        actions.addWidget(self.btn_browse_score)
        actions.addWidget(self.btn_demo_score)

        drop_layout.addWidget(lbl_icon)
        drop_layout.addWidget(lbl_main)
        drop_layout.addWidget(lbl_formats)
        drop_layout.addSpacing(5)
        drop_layout.addLayout(actions)
        layout.addWidget(self.drop_card, 0, Qt.AlignCenter)
        self.view_stack.addWidget(self.landing_view)

    def _build_player_view(self) -> None:
        self.player_view = QWidget()
        layout = QVBoxLayout(self.player_view)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(7)

        self.info_card = QFrame(self.player_view)
        self.info_card.setObjectName("trackHeader")
        info_layout = QHBoxLayout(self.info_card)
        info_layout.setContentsMargins(12, 7, 12, 7)
        info_layout.setSpacing(9)

        self.lbl_song_title = ElidedLabel("곡 제목", self.info_card)
        self.lbl_song_title.setObjectName("trackTitle")
        self.lbl_song_title.setMinimumWidth(90)
        self.lbl_song_title.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Preferred)
        info_layout.addWidget(self.lbl_song_title, 1)

        self.lbl_stat_dur = QLabel("00:00", self.info_card)
        self.lbl_stat_dur.setObjectName("metricValue")
        self.lbl_stat_bpm = QLabel("120 BPM", self.info_card)
        self.lbl_stat_bpm.setObjectName("metricValue")
        self.lbl_stat_notes = QLabel("0 노트", self.info_card)
        self.lbl_stat_notes.setObjectName("metricValue")
        self.lbl_stat_range = QLabel("A0–C8", self.info_card)
        self.lbl_stat_range.setObjectName("rangeValue")
        self.lbl_stat_range.setProperty("state", "success")
        for label in (self.lbl_stat_dur, self.lbl_stat_bpm, self.lbl_stat_notes, self.lbl_stat_range):
            info_layout.addWidget(label)

        self.btn_octave_fit = QPushButton("옥타브 맞춤")
        self.btn_octave_fit.setObjectName("secondaryAction")
        self.btn_octave_fit.clicked.connect(self._apply_octave_fit)
        info_layout.addWidget(self.btn_octave_fit)

        self.btn_open_score = QPushButton("파일 열기")
        self.btn_open_score.setObjectName("secondaryAction")
        self.btn_open_score.clicked.connect(self._browse_file)
        info_layout.addWidget(self.btn_open_score)
        layout.addWidget(self.info_card)

        self.lbl_countdown_banner = QLabel("")
        self.lbl_countdown_banner.setObjectName("countdownBanner")
        self.lbl_countdown_banner.setAlignment(Qt.AlignCenter)
        self.lbl_countdown_banner.hide()
        layout.addWidget(self.lbl_countdown_banner)

        self.piano_roll = PianoRollWidget(self.player_view)
        self.piano_roll.seek_requested.connect(self._handle_seek)
        self.piano_roll.setMinimumHeight(120)
        layout.addWidget(self.piano_roll, 1)

        self.virtual_piano = VirtualPianoWidget(self.mapper, self.player_view)
        self.virtual_piano.key_clicked.connect(self._handle_piano_key_clicked)
        self.virtual_piano.setMinimumHeight(88)
        self.virtual_piano.setMaximumHeight(120)
        layout.addWidget(self.virtual_piano)

        self.controls_card = QFrame(self.player_view)
        self.controls_card.setObjectName("ControlDeck")
        self.controls_card.setFixedHeight(88)
        deck = QVBoxLayout(self.controls_card)
        deck.setContentsMargins(10, 6, 10, 5)
        deck.setSpacing(2)

        controls_row = QHBoxLayout()
        controls_row.setContentsMargins(0, 0, 0, 0)
        controls_row.setSpacing(8)

        left_controls = QWidget(self.controls_card)
        left_layout = QVBoxLayout(left_controls)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(2)
        hand_row = QHBoxLayout()
        hand_row.setContentsMargins(0, 0, 0, 0)
        hand_row.setSpacing(4)
        self.chk_rh = QCheckBox("R  오른손")
        self.chk_rh.setObjectName("handToggle")
        self.chk_rh.setChecked(self.config.enable_rh)
        self.chk_lh = QCheckBox("L  왼손")
        self.chk_lh.setObjectName("handToggle")
        self.chk_lh.setChecked(self.config.enable_lh)
        self.chk_rh.toggled.connect(self._toggle_hands)
        self.chk_lh.toggled.connect(self._toggle_hands)
        hand_row.addWidget(self.chk_lh)
        hand_row.addWidget(self.chk_rh)
        left_layout.addLayout(hand_row)

        transpose_row = QHBoxLayout()
        transpose_row.setContentsMargins(0, 0, 0, 0)
        transpose_row.setSpacing(3)
        lbl_transpose = QLabel("조옮김")
        lbl_transpose.setObjectName("guideText")
        self.btn_transpose_down = QPushButton("−")
        self.btn_transpose_down.setObjectName("smallControl")
        self.btn_transpose_down.clicked.connect(
            lambda: self.slider_transpose.setValue(self.slider_transpose.value() - 1)
        )
        self.lbl_transpose_val = QLabel("0 st")
        self.lbl_transpose_val.setObjectName("numericValue")
        self.lbl_transpose_val.setAlignment(Qt.AlignCenter)
        self.lbl_transpose_val.setMinimumWidth(42)
        self.slider_transpose = QSlider(Qt.Horizontal, self.controls_card)
        self.slider_transpose.setRange(-12, 12)
        self.slider_transpose.setValue(0)
        self.slider_transpose.valueChanged.connect(self._on_transpose_changed)
        self.slider_transpose.hide()
        self.btn_transpose_up = QPushButton("+")
        self.btn_transpose_up.setObjectName("smallControl")
        self.btn_transpose_up.clicked.connect(
            lambda: self.slider_transpose.setValue(self.slider_transpose.value() + 1)
        )
        self.btn_reset_transpose = QPushButton("0")
        self.btn_reset_transpose.setObjectName("smallControl")
        self.btn_reset_transpose.setToolTip("조옮김 초기화")
        self.btn_reset_transpose.clicked.connect(lambda: self.slider_transpose.setValue(0))
        transpose_row.addWidget(lbl_transpose)
        transpose_row.addWidget(self.btn_transpose_down)
        transpose_row.addWidget(self.lbl_transpose_val)
        transpose_row.addWidget(self.btn_transpose_up)
        transpose_row.addWidget(self.btn_reset_transpose)
        left_layout.addLayout(transpose_row)
        controls_row.addWidget(left_controls, 1)

        transport = QWidget(self.controls_card)
        transport_layout = QHBoxLayout(transport)
        transport_layout.setContentsMargins(0, 0, 0, 0)
        transport_layout.setSpacing(5)
        self.btn_main_play = QPushButton("재생   F6")
        self.btn_main_play.setObjectName("primary_btn")
        self.btn_main_play.setFixedHeight(36)
        self.btn_main_play.setMinimumWidth(112)
        self.btn_main_play.clicked.connect(self._handle_play_button_click)
        self.btn_main_stop = QPushButton("정지   F8")
        self.btn_main_stop.setFixedHeight(36)
        self.btn_main_stop.setMinimumWidth(72)
        self.btn_main_stop.clicked.connect(self._handle_stop_button_click)
        transport_layout.addWidget(self.btn_main_play)
        transport_layout.addWidget(self.btn_main_stop)
        controls_row.addWidget(transport, 0, Qt.AlignCenter)

        right_controls = QWidget(self.controls_card)
        speed_layout = QVBoxLayout(right_controls)
        speed_layout.setContentsMargins(0, 0, 0, 0)
        speed_layout.setSpacing(2)
        speed_row = QHBoxLayout()
        speed_row.setContentsMargins(0, 0, 0, 0)
        speed_row.setSpacing(4)
        lbl_speed = QLabel("속도")
        lbl_speed.setObjectName("guideText")
        speed_row.addWidget(lbl_speed)
        self.slider_speed = QSlider(Qt.Horizontal)
        self.slider_speed.setRange(25, 200)
        self.slider_speed.setValue(int(round(self.config.playback_speed * 100)))
        self.slider_speed.setMinimumWidth(74)
        self.slider_speed.setMaximumWidth(104)
        self.lbl_speed_val = QLabel(f"{self.config.playback_speed:.2f}×")
        self.lbl_speed_val.setObjectName("numericValue")
        self.lbl_speed_val.setFixedWidth(48)
        self.lbl_speed_val.setAlignment(Qt.AlignRight | Qt.AlignVCenter)
        self.slider_speed.valueChanged.connect(self._on_speed_slider_changed)
        speed_row.addWidget(self.slider_speed, 1)
        speed_row.addWidget(self.lbl_speed_val)
        speed_layout.addLayout(speed_row)

        self.btn_speed_reset = QPushButton("1.00×")
        self.btn_speed_reset.setObjectName("secondaryAction")
        self.btn_speed_reset.setFixedHeight(24)
        self.btn_speed_reset.setToolTip("기본 속도로 설정")
        self.btn_speed_reset.clicked.connect(lambda: self.slider_speed.setValue(100))
        speed_layout.addWidget(self.btn_speed_reset, 0, Qt.AlignRight)
        controls_row.addWidget(right_controls, 1)

        deck.addLayout(controls_row, 1)
        self.lbl_guide_text = QLabel("F6 재생  ·  F7 일시 정지/계속  ·  클릭하거나 드래그해 재생 위치 이동")
        self.lbl_guide_text.setObjectName("guideText")
        deck.addWidget(self.lbl_guide_text)
        layout.addWidget(self.controls_card)
        self.view_stack.addWidget(self.player_view)

    def _setup_overlay(self) -> None:
        self.overlay = FloatingOverlay()
        self.overlay.move(self.config.overlay_pos_x, self.config.overlay_pos_y)
        self.overlay.setWindowOpacity(self.config.overlay_opacity)
        self.overlay.set_click_through(self.config.overlay_click_through)

        self.overlay.play_requested.connect(self._handle_play_button_click)
        self.overlay.stop_requested.connect(self._handle_stop_button_click)
        self.overlay.mode_toggled.connect(lambda comp: setattr(self.config, "overlay_compact", comp))

        if self.config.overlay_compact:
            self.overlay.toggle_compact_mode()

        self.overlay.hide()

    def _connect_signals(self) -> None:
        self.sig_state_changed.connect(self._on_playback_state_changed)
        self.sig_progress.connect(self._on_playback_progress)
        self.sig_countdown.connect(self._on_playback_countdown)
        self.sig_notes_played.connect(self._on_notes_played)

    def _apply_theme(self) -> None:
        self.setStyleSheet(get_stylesheet(self.config.theme))
        if hasattr(self, "library_widget"):
            from src.app.library_visual_v3 import get_library_stylesheet
            self.library_widget.setStyleSheet(get_library_stylesheet(self.config.theme))

    def _register_hotkeys(self) -> None:
        self.hotkeys.update_hotkeys(
            play_hk=self.config.hotkey_play,
            pause_hk=self.config.hotkey_pause,
            stop_hk=self.config.hotkey_stop,
            overlay_hk=self.config.hotkey_overlay
        )
        self.hotkeys.register(
            on_play=lambda: self.sig_state_changed.emit("HOTKEY_PLAY"),
            on_pause=lambda: self.sig_state_changed.emit("HOTKEY_PAUSE"),
            on_stop=lambda: self.sig_state_changed.emit("HOTKEY_STOP"),
            on_toggle_overlay=lambda: self.sig_state_changed.emit("HOTKEY_OVERLAY")
        )

    # ----------------------------------------------------
    # File Loading & Importers
    # ----------------------------------------------------
    def dragEnterEvent(self, event: QDragEnterEvent) -> None:
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event: QDropEvent) -> None:
        urls = event.mimeData().urls()
        if urls:
            for url in urls:
                file_path = url.toLocalFile()
                self._import_file_to_library_and_parse(file_path)

    def _browse_file(self) -> None:
        filters = (
            "All Supported Scores (*.mid *.midi *.musicxml *.xml *.mxl *.txt *.png *.jpg *.jpeg *.pdf);;"
            "MIDI Files (*.mid *.midi);;"
            "MusicXML Files (*.musicxml *.xml *.mxl);;"
            "Numeric / Jianpu (*.txt *.num);;"
            "Score Images (*.png *.jpg *.jpeg);;"
            "PDF Scores (*.pdf)"
        )
        path, _ = QFileDialog.getOpenFileName(self, "Open Score File", self.config.last_directory, filters)
        if path:
            self.config.last_directory = os.path.dirname(path)
            self.load_score_file(path)

    def _load_sample_score(self) -> None:
        """Loads a built-in multi-hand numerical demo of Canon in D."""
        sample_jianpu = (
            "[1 5 1'] - [7, 5 7] - [6, 3 6] - [5, 3 5] - "
            "[4, 1 4] - [3, 1 3] - [4, 1 4] - [5, 2 5] - "
            "[1 5 1'] [3 5] [7, 5 7] [2 5] [6, 3 6] [1 3] [5, 3 5] [7, 2] "
            "[4, 1 4] [6, 1] [3, 1 3] [5, 1] [4, 1 4] [6, 1] [5, 2 5] [7, 2]"
        )
        timeline = self.numeric_importer.import_score(sample_jianpu, tonic="D", base_octave=4, bpm=90.0)
        timeline.title = "Canon in D (Sample Demo)"
        self._set_loaded_timeline(timeline)

    def parse_score_file(self, file_path: str) -> Optional[MusicTimeline]:
        if not os.path.isfile(file_path):
            QMessageBox.warning(self, "파일 오류", f"파일을 찾을 수 없습니다: {file_path}")
            return None

        ext = os.path.splitext(file_path)[1].lower()
        try:
            timeline = None
            if ext in (".mid", ".midi"):
                from src.importers.midi_importer import MidiImporter
                timeline = MidiImporter().import_score(file_path)
            elif ext in (".musicxml", ".xml", ".mxl"):
                from src.importers.musicxml_importer import MusicXmlImporter
                timeline = MusicXmlImporter().import_score(file_path)
            elif ext in (".txt", ".num", ".jianpu"):
                timeline = self.numeric_importer.import_score(file_path)
            elif ext in (".png", ".jpg", ".jpeg", ".bmp", ".tiff"):
                from src.importers.image_importer import ImageImporter
                timeline = ImageImporter().import_score(file_path)
            elif ext in (".pdf",):
                from src.importers.pdf_importer import PdfImporter
                timeline = PdfImporter().import_score(file_path)
            else:
                QMessageBox.warning(self, "지원하지 않는 포맷", f"지원하지 않는 파일 확장자입니다: {ext}")
                return None
            return timeline
        except Exception as e:
            QMessageBox.critical(self, "불러오기 오류", f"악보를 불러오는데 실패했습니다:\n{str(e)}")
            return None

    def load_score_file(self, file_path: str) -> None:
        self._queue_score_load(file_path)

    def _queue_score_load(
        self,
        file_path: str,
        library_item: Optional[ScoreItem] = None,
    ) -> None:
        if not os.path.isfile(file_path):
            QMessageBox.warning(self, "파일 오류", f"파일을 찾을 수 없습니다:\n{file_path}")
            return
        self._score_load_queue.append((file_path, library_item))
        self._start_next_score_load()

    def _start_next_score_load(self) -> None:
        if self._closing_after_workers or self._score_load_worker is not None:
            return
        if not self._score_load_queue:
            return

        file_path, library_item = self._score_load_queue.popleft()
        worker = ScoreLoadWorker(file_path, self)
        worker.finished.connect(
            lambda worker=worker, path=file_path, item=library_item:
                self._on_score_load_worker_finished(worker, path, item)
        )
        self._score_load_worker = worker
        self.statusBar().showMessage(f"악보 불러오는 중: {os.path.basename(file_path)}")
        worker.start()

    def _on_score_load_worker_finished(
        self,
        worker: ScoreLoadWorker,
        file_path: str,
        library_item: Optional[ScoreItem],
    ) -> None:
        if worker is not self._score_load_worker:
            return

        if not self._closing_after_workers:
            if worker.timeline is not None and not worker.error:
                self._set_loaded_timeline(worker.timeline)
                self.statusBar().showMessage(
                    f"불러옴: {worker.timeline.title or os.path.basename(file_path)}", 5000
                )
                if library_item is not None:
                    try:
                        self.library_manager.update_score_from_timeline(
                            library_item.id, worker.timeline
                        )
                        self.library_widget.refresh_library()
                    except Exception as exc:
                        logger.log(f"Library metadata update failed: {exc}")
                    self.tabs.setCurrentIndex(0)
            else:
                message = worker.error or "악보를 읽을 수 없습니다."
                if library_item is not None:
                    ext = os.path.splitext(file_path)[1].lower()
                    library_item.analysis_status = (
                        "ANALYSIS_REQUIRED"
                        if ext in (".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tiff")
                        else "ANALYSIS_FAILED"
                    )
                    library_item.analysis_error = message
                    self.library_manager.db.update_score(library_item)
                    self.library_widget.refresh_library()
                self.statusBar().showMessage("악보를 불러오지 못했습니다.", 5000)
                QMessageBox.critical(self, "불러오기 오류", f"악보를 불러오지 못했습니다:\n{message}")

        self._score_load_worker = None
        worker.deleteLater()
        if self._closing_after_workers:
            self._maybe_finish_close()
        else:
            QTimer.singleShot(0, self._start_next_score_load)

    def _import_file_to_library_and_parse(self, file_path: str):
        # 1. First, copy file into library and register it
        try:
            score_item = self.library_manager.import_external_file(file_path, "FILE")
        except Exception as e:
            QMessageBox.warning(self, "가져오기 실패", f"파일을 라이브러리에 복사할 수 없습니다: {e}")
            return
            
        # 2. Refresh library to show the item
        self.library_widget.refresh_library()
        
        # 3. Parse timeline
        self._queue_score_load(score_item.filepath, score_item)

    def _on_library_add_external(self):
        filters = (
            "All Supported Scores (*.mid *.midi *.musicxml *.xml *.mxl *.txt *.png *.jpg *.jpeg *.pdf);;"
            "MIDI Files (*.mid *.midi);;"
            "MusicXML Files (*.musicxml *.xml *.mxl);;"
            "Numeric / Jianpu (*.txt *.num);;"
            "Score Images (*.png *.jpg *.jpeg);;"
            "PDF Scores (*.pdf)"
        )
        paths, _ = QFileDialog.getOpenFileNames(self, "외부 파일 추가", self.config.last_directory, filters)
        if paths:
            self.config.last_directory = os.path.dirname(paths[0])
            for path in paths:
                self._import_file_to_library_and_parse(path)
            QMessageBox.information(self, "성공", f"{len(paths)}개의 악보가 라이브러리에 추가되었습니다.")

    def _on_library_score_selected(self, item: ScoreItem):
        # Library item selected
        if os.path.exists(item.filepath):
            self._queue_score_load(item.filepath, item)
        else:
            QMessageBox.warning(self, "파일 없음", f"라이브러리 파일을 찾을 수 없습니다:\n{item.filepath}")

    def _on_import_requested(self, source_type: str, source_path: str):
        if self.active_worker and self.active_worker.isRunning():
            QMessageBox.warning(self, "작업 중", "이미 다른 분석 작업이 진행 중입니다.")
            return

        self.lbl_background_task.setText("동영상 분석")
        self.lbl_background_task.setToolTip(source_path)
        self.lbl_background_task.show()
        self.background_task_progress.setValue(0)
        self.background_task_progress.show()
        self.statusBar().showMessage("동영상 분석을 시작했습니다.", 4000)

        self.active_worker = VideoAnalysisWorker(
            source_type=source_type,
            source_path=source_path,
            library_manager=self.library_manager,
            parent=self,
        )
        worker = self.active_worker
        worker.progress.connect(self.import_widget.update_progress)
        worker.progress.connect(self._on_background_import_progress)
        worker.finished_success.connect(self._on_import_success)
        worker.finished_error.connect(self._on_import_error)
        worker.finished.connect(lambda worker=worker: self._on_video_worker_finished(worker))
        self.active_worker.start()

    def _on_background_import_progress(self, percent: int, status_text: str) -> None:
        self.background_task_progress.setValue(max(0, min(100, percent)))
        self.lbl_background_task.setToolTip(status_text)
        self.lbl_background_task.setText("분석 중")

    def _on_import_success(self, item: ScoreItem):
        self.import_widget.task_finished(
            True, f"{item.title} · 라이브러리에 저장됨", item
        )
        self.library_widget.refresh_library()
        self.statusBar().showMessage("악보 추출이 완료되었습니다.", 5000)

    def _on_import_error(self, err_msg: str):
        self.import_widget.task_finished(False, err_msg)
        self.statusBar().showMessage("동영상 분석에 실패했습니다.", 5000)

    def _cancel_active_video_import(self) -> None:
        if self.active_worker and self.active_worker.isRunning():
            self.active_worker.cancel()

    def _on_video_worker_finished(self, worker: VideoAnalysisWorker) -> None:
        if self.active_worker is worker:
            self.active_worker = None
        self.lbl_background_task.hide()
        self.background_task_progress.hide()
        worker.deleteLater()
        if self._closing_after_workers:
            self._maybe_finish_close()

    def _set_loaded_timeline(self, timeline: MusicTimeline) -> None:
        self.timeline = timeline
        self.scheduler.set_timeline(timeline)

        title = timeline.title or "제목 없음"
        self.lbl_song_title.set_elided_text(title)
        self.overlay.set_song_title(title)

        dur_min, dur_sec = divmod(int(timeline.duration), 60)
        self.lbl_stat_dur.setText(f"{dur_min:02d}:{dur_sec:02d}")
        self.lbl_stat_bpm.setText(f"{int(timeline.initial_bpm)} BPM")
        self.lbl_stat_notes.setText(f"{timeline.total_notes:,} 노트")

        analysis = RangeProcessor.analyze_range(timeline)
        pitch_range = f"{_pitch_label(analysis.min_pitch)}–{_pitch_label(analysis.max_pitch)}"
        if analysis.out_of_range_count > 0:
            self.lbl_stat_range.setText(
                f"{pitch_range} · 범위 밖 {analysis.out_of_range_count:,}"
            )
            self.lbl_stat_range.setProperty("state", "warning")
            self.btn_octave_fit.show()
        else:
            self.lbl_stat_range.setText(pitch_range)
            self.lbl_stat_range.setProperty("state", "success")
            self.btn_octave_fit.hide()
        self.lbl_stat_range.style().unpolish(self.lbl_stat_range)
        self.lbl_stat_range.style().polish(self.lbl_stat_range)

        self.piano_roll.set_timeline(timeline)
        self.view_stack.setCurrentIndex(1)

    def _go_back_to_landing(self) -> None:
        self.scheduler.stop()
        self.view_stack.setCurrentIndex(0)

    # ----------------------------------------------------
    # Range & Octave Fit
    # ----------------------------------------------------
    def _apply_octave_fit(self) -> None:
        if not self.timeline:
            return
        modified = RangeProcessor.apply_octave_fit(self.timeline)
        self.piano_roll.update()
        analysis = RangeProcessor.analyze_range(self.timeline)
        self.lbl_stat_range.setText(
            f"{_pitch_label(analysis.min_pitch)}–{_pitch_label(analysis.max_pitch)}"
        )
        self.lbl_stat_range.setProperty("state", "success")
        self.lbl_stat_range.style().unpolish(self.lbl_stat_range)
        self.lbl_stat_range.style().polish(self.lbl_stat_range)
        self.btn_octave_fit.hide()
        self.statusBar().showMessage(
            f"옥타브 맞춤 완료 · {modified:,}개 음표 조정", 5000
        )

    # ----------------------------------------------------
    # Playback Control Handlers
    # ----------------------------------------------------
    def _handle_play_button_click(self) -> None:
        if self.scheduler.state in (PlaybackState.IDLE, PlaybackState.STOPPED, PlaybackState.COMPLETED):
            # Target focus check before starting
            can_play, reason = self.target_window.check_can_play()
            if not can_play and self.config.target_window_safety and not self.config.dry_run_mode:
                QMessageBox.information(
                    self, "로블록스 활성화 확인",
                    f"{reason}\n\n로블록스 창을 활성화(클릭)한 뒤 F6을 눌러 시작해주세요!"
                )
                return
            self.scheduler.play()
        elif self.scheduler.state == PlaybackState.PLAYING:
            self.scheduler.pause()
        elif self.scheduler.state == PlaybackState.PAUSED:
            self.scheduler.resume()

    def _handle_stop_button_click(self) -> None:
        self.scheduler.stop()
        self.piano_roll.set_playhead_time(0.0)
        self.virtual_piano.set_active_pitches(set())

    def _handle_seek(self, target_time: float) -> None:
        self.scheduler.seek(target_time)
        self.piano_roll.set_playhead_time(target_time)

    def _toggle_hands(self) -> None:
        rh = self.chk_rh.isChecked()
        lh = self.chk_lh.isChecked()
        self.scheduler.enable_rh = rh
        self.scheduler.enable_lh = lh
        self.overlay.set_hands(rh, lh)

    def _on_speed_slider_changed(self, val: int) -> None:
        spd = val / 100.0
        self.lbl_speed_val.setText(f"{spd:.2f}×")
        self.scheduler.set_speed(spd)
        self.overlay.set_speed(spd)

    def _on_transpose_changed(self, semitones: int) -> None:
        self.lbl_transpose_val.setText(f"{semitones:+d} st" if semitones != 0 else "0 st")
        if self.timeline:
            Transposer.transpose(self.timeline, semitones)
            self.piano_roll.update()

    def _handle_piano_key_clicked(self, pitch: int, km) -> None:
        """Testing single note input via DryRun/SendInput"""
        logger.log(f"Key Test: {km.name} -> Key: '{km.char}' (Shift={km.shift})")
        ev = NoteEvent(pitch=pitch, start_time=0.0, end_time=0.1)
        self.chord_engine.play_chord_notes([ev], hold_duration_ms=60)
        self.virtual_piano.set_active_pitches({pitch})
        self._active_notes_timer.start()

    # ----------------------------------------------------
    # Slot Callbacks for Playback Signals
    # ----------------------------------------------------
    @Slot(str)
    def _on_playback_state_changed(self, state_str: str) -> None:
        if state_str == "HOTKEY_PLAY":
            self._handle_play_button_click()
            return
        elif state_str == "HOTKEY_PAUSE":
            self.scheduler.toggle_play_pause()
            return
        elif state_str == "HOTKEY_STOP":
            self._handle_stop_button_click()
            return
        elif state_str == "HOTKEY_OVERLAY":
            self._toggle_overlay()
            return

        try:
            state = PlaybackState(state_str)
        except ValueError:
            return

        self.overlay.set_playback_state(state)
        if state == PlaybackState.PLAYING:
            self.lbl_status_badge.setText("연주 중")
            self.lbl_status_badge.setProperty("state", "playing")
            self.btn_main_play.setText("일시 정지   F7")
        elif state == PlaybackState.PAUSED:
            self.lbl_status_badge.setText("일시 정지")
            self.lbl_status_badge.setProperty("state", "paused")
            self.btn_main_play.setText("계속   F7")
        elif state == PlaybackState.COUNTDOWN:
            self.lbl_status_badge.setText("시작 대기")
            self.lbl_status_badge.setProperty("state", "countdown")
            self.btn_main_play.setText("재생   F6")
        else:
            self.lbl_status_badge.setText("준비됨")
            self.lbl_status_badge.setProperty("state", "ready")
            self.btn_main_play.setText("재생   F6")

        self.lbl_status_badge.style().unpolish(self.lbl_status_badge)
        self.lbl_status_badge.style().polish(self.lbl_status_badge)

    @Slot(float, float)
    def _on_playback_progress(self, current: float, total: float) -> None:
        self.piano_roll.set_playhead_time(current)
        self.overlay.set_progress(current, total)

    @Slot(int)
    def _on_playback_countdown(self, sec: int) -> None:
        self.overlay.set_countdown(sec)
        if sec > 0:
            self.lbl_countdown_banner.setText(
                f"{sec}초 후 연주 시작 · 지금 Roblox 창을 클릭해 포커스를 맞추세요"
            )
            self.lbl_countdown_banner.show()
        else:
            self.lbl_countdown_banner.hide()

    @Slot(list)
    def _on_notes_played(self, notes: List[NoteEvent]) -> None:
        pitches = {n.pitch for n in notes}
        self.virtual_piano.set_active_pitches(pitches)
        self._active_notes_timer.start()

    def _clear_active_piano_keys(self) -> None:
        self._active_notes_timer.stop()
        self.virtual_piano.set_active_pitches(set())

    def _update_roblox_focus_status(self) -> None:
        foreground = self.target_window.is_roblox_foreground()
        if foreground == self._last_roblox_foreground:
            return
        self._last_roblox_foreground = foreground

        if foreground:
            self.lbl_roblox_status.setText("● Roblox 활성")
            self.lbl_roblox_status.setProperty("state", "active")
            self.lbl_roblox_status.setToolTip("현재 Roblox 창에 키 입력을 보낼 수 있습니다")
        else:
            self.lbl_roblox_status.setText("○ Roblox 대기")
            self.lbl_roblox_status.setProperty("state", "waiting")
            self.lbl_roblox_status.setToolTip("연주를 시작하면 Roblox 창을 자동으로 확인합니다")
        self.lbl_roblox_status.style().unpolish(self.lbl_roblox_status)
        self.lbl_roblox_status.style().polish(self.lbl_roblox_status)

    def _toggle_overlay(self) -> None:
        if self.overlay.isVisible():
            self.overlay.hide()
            self.btn_overlay_toggle.setChecked(False)
        else:
            self.overlay.show()
            self.btn_overlay_toggle.setChecked(True)

    def _open_settings(self) -> None:
        dlg = SettingsDialog(self.config_mgr, self)
        dlg.settings_saved.connect(self._on_settings_saved)
        dlg.exec()

    def _on_settings_saved(self, new_config: AppConfig) -> None:
        self.config = new_config
        self._apply_theme()
        self._register_hotkeys()
        self.overlay.setWindowOpacity(new_config.overlay_opacity)
        self.overlay.set_click_through(new_config.overlay_click_through)

        # Update scheduler & target window
        self.scheduler.countdown_seconds = new_config.countdown_seconds
        self.chord_engine.conflict_policy = ConflictPolicy(new_config.conflict_policy)
        self.chord_engine.conflict_delay_ms = new_config.conflict_delay_ms
        self.chord_engine.default_hold_duration_ms = new_config.hold_duration_ms
        self.target_window.enabled = new_config.target_window_safety
        self.target_window.policy = FocusLossPolicy(new_config.focus_loss_policy)
        
        self.pedal_backend.setup(
            enabled=new_config.pedal_enabled,
            x_ratio=new_config.pedal_x_ratio,
            y_ratio=new_config.pedal_y_ratio,
            interaction_mode=new_config.pedal_mode,
            restore_cursor=True
        )

    # ----------------------------------------------------
    # Safe Application Termination
    # ----------------------------------------------------
    def _background_workers_running(self) -> bool:
        if self._score_load_worker and self._score_load_worker.isRunning():
            return True
        if self.active_worker and self.active_worker.isRunning():
            return True
        return bool(self.library_widget.running_background_workers())

    def _maybe_finish_close(self) -> None:
        if not self._closing_after_workers:
            return
        if self._background_workers_running():
            QTimer.singleShot(250, self._maybe_finish_close)
            return
        self._closing_after_workers = False
        self.close()

    def closeEvent(self, event) -> None:
        """Ensures all keys are released, scheduler stopped, and hotkeys unhooked."""
        if self._background_workers_running():
            self._closing_after_workers = True
            self._score_load_queue.clear()
            if self._score_load_worker and self._score_load_worker.isRunning():
                self._score_load_worker.requestInterruption()
            if self.active_worker and self.active_worker.isRunning():
                self.active_worker.cancel()
            self.library_widget.cancel_background_imports()
            self.statusBar().showMessage(
                "백그라운드 작업을 정리한 뒤 창을 닫습니다."
            )
            QTimer.singleShot(250, self._maybe_finish_close)
            event.ignore()
            return

        self._closing_after_workers = False
        self.scheduler.stop()
        self.key_state.release_all()
        self.hotkeys.unregister_all()
        if self.overlay:
            # Save overlay position
            self.config.overlay_pos_x = self.overlay.x()
            self.config.overlay_pos_y = self.overlay.y()
            self.config_mgr.save_config(self.config)
            self.overlay.close()
        event.accept()
