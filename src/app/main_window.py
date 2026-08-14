"""
Roblox Piano Player - Main Application Window
"""
import os
from typing import Optional, List, Dict
from PySide6.QtCore import Qt, QTimer, Signal, Slot
from PySide6.QtGui import QDragEnterEvent, QDropEvent, QIcon, QFont
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QStackedWidget, QFileDialog, QSlider, QFrame, QMessageBox, QDialog,
    QScrollArea, QCheckBox
)

from src.music.events import NoteEvent, HandType
from src.music.timeline import MusicTimeline
from src.music.transpose import Transposer
from src.music.range_processor import RangeProcessor, RangeAnalysisResult
from src.piano.mapper import RobloxPianoMapper
from src.importers.midi_importer import MidiImporter
from src.importers.musicxml_importer import MusicXmlImporter
from src.importers.numeric_importer import NumericImporter
from src.importers.image_importer import ImageImporter
from src.importers.pdf_importer import PdfImporter
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

        # Importers
        self.midi_importer = MidiImporter()
        self.musicxml_importer = MusicXmlImporter()
        self.numeric_importer = NumericImporter()
        self.image_importer = ImageImporter()
        self.pdf_importer = PdfImporter()

        # State
        self.timeline: Optional[MusicTimeline] = None
        self._active_notes_timer = QTimer(self)
        self._active_notes_timer.setInterval(100)
        self._active_notes_timer.timeout.connect(self._clear_active_piano_keys)

        # Hotkeys
        self.hotkeys = GlobalHotkeyManager()

        # 2. UI Setup
        self._setup_ui()
        self._setup_overlay()
        self._connect_signals()
        self._apply_theme()
        self._register_hotkeys()

        # Timer for polling Roblox window focus status
        self._focus_poll_timer = QTimer(self)
        self._focus_poll_timer.setInterval(500)
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
        self.root_layout.setContentsMargins(20, 16, 20, 16)
        self.root_layout.setSpacing(14)

        # Top Navigation Bar
        self.top_bar = QHBoxLayout()
        self.lbl_app_title = QLabel("로블록스 피아노 연주기")
        self.lbl_app_title.setObjectName("title_label")

        self.lbl_status_badge = QLabel("준비됨")
        self.lbl_status_badge.setStyleSheet(
            "background-color: #2D3748; color: #E2E8F0; font-weight: bold; padding: 4px 10px; border-radius: 6px; font-size: 11px;"
        )

        self.lbl_roblox_status = QLabel("● 로블록스 확인 중...")
        self.lbl_roblox_status.setStyleSheet("color: #94A3B8; font-size: 12px; margin-left: 10px;")

        self.btn_overlay_toggle = QPushButton("오버레이 (F4)")
        self.btn_overlay_toggle.setObjectName("accent_toggle")
        self.btn_overlay_toggle.setCheckable(True)
        self.btn_overlay_toggle.setChecked(True)
        self.btn_overlay_toggle.clicked.connect(self._toggle_overlay)

        self.btn_settings = QPushButton("⚙ 설정")
        self.btn_settings.clicked.connect(self._open_settings)

        self.top_bar.addWidget(self.lbl_app_title)
        self.top_bar.addWidget(self.lbl_status_badge)
        self.top_bar.addWidget(self.lbl_roblox_status)
        self.top_bar.addStretch()
        self.top_bar.addWidget(self.btn_overlay_toggle)
        self.top_bar.addWidget(self.btn_settings)

        self.root_layout.addLayout(self.top_bar)

        # Stacked Views (0: Landing Drop View, 1: Player View)
        self.view_stack = QStackedWidget(self)
        self._build_landing_view()
        self._build_player_view()
        self.root_layout.addWidget(self.view_stack, 1)

    def _build_landing_view(self) -> None:
        self.landing_view = QWidget()
        layout = QVBoxLayout(self.landing_view)
        layout.setContentsMargins(0, 10, 0, 0)
        layout.setAlignment(Qt.AlignCenter)

        self.drop_card = QFrame()
        self.drop_card.setObjectName("drop_card")
        self.drop_card.setMinimumSize(600, 360)
        drop_layout = QVBoxLayout(self.drop_card)
        drop_layout.setAlignment(Qt.AlignCenter)
        drop_layout.setSpacing(16)

        lbl_icon = QLabel("🎹")
        lbl_icon.setStyleSheet("font-size: 48px;")
        lbl_icon.setAlignment(Qt.AlignCenter)

        lbl_main = QLabel("여기에 악보 파일을 드래그 앤 드롭하세요")
        lbl_main.setStyleSheet("font-size: 20px; font-weight: bold; color: #FFFFFF;")
        lbl_main.setAlignment(Qt.AlignCenter)

        lbl_formats = QLabel("지원 포맷: MIDI (.mid)  •  MusicXML (.xml, .mxl)  •  숫자악보 (.txt)  •  이미지 / PDF")
        lbl_formats.setStyleSheet("font-size: 13px; color: #8C93A0;")
        lbl_formats.setAlignment(Qt.AlignCenter)

        btn_browse = QPushButton("악보 파일 찾기...")
        btn_browse.setObjectName("primary_btn")
        btn_browse.setFixedWidth(200)
        btn_browse.clicked.connect(self._browse_file)

        btn_sample = QPushButton("데모 곡 불러오기 (캐논 변주곡)")
        btn_sample.setFixedWidth(200)
        btn_sample.clicked.connect(self._load_sample_score)

        drop_layout.addWidget(lbl_icon)
        drop_layout.addWidget(lbl_main)
        drop_layout.addWidget(lbl_formats)
        drop_layout.addSpacing(10)
        drop_layout.addWidget(btn_browse, 0, Qt.AlignCenter)
        drop_layout.addWidget(btn_sample, 0, Qt.AlignCenter)

        layout.addWidget(self.drop_card)
        self.view_stack.addWidget(self.landing_view)

    def _build_player_view(self) -> None:
        self.player_view = QWidget()
        layout = QVBoxLayout(self.player_view)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        # Header Info Card
        self.info_card = QFrame()
        self.info_card.setObjectName("card")
        info_layout = QHBoxLayout(self.info_card)
        info_layout.setContentsMargins(16, 12, 16, 12)

        # Left: Back btn & Song title
        left_info = QVBoxLayout()
        back_row = QHBoxLayout()
        self.btn_back = QPushButton("← 다른 악보 불러오기")
        self.btn_back.setStyleSheet("font-size: 12px; padding: 4px 8px;")
        self.btn_back.clicked.connect(self._go_back_to_landing)
        self.lbl_song_title = QLabel("곡 제목")
        self.lbl_song_title.setStyleSheet("font-size: 16px; font-weight: bold; color: #FFFFFF;")
        back_row.addWidget(self.btn_back)
        back_row.addWidget(self.lbl_song_title, 1)
        left_info.addLayout(back_row)
        info_layout.addLayout(left_info, 2)

        # Right: Stats (Duration, BPM, Notes, Range)
        stats_layout = QHBoxLayout()
        stats_layout.setSpacing(20)

        # Duration
        v1 = QVBoxLayout()
        self.lbl_stat_dur = QLabel("00:00")
        self.lbl_stat_dur.setObjectName("stat_value")
        l1 = QLabel("재생 시간"); l1.setObjectName("stat_desc")
        v1.addWidget(self.lbl_stat_dur); v1.addWidget(l1)

        # BPM
        v2 = QVBoxLayout()
        self.lbl_stat_bpm = QLabel("120")
        self.lbl_stat_bpm.setObjectName("stat_value")
        l2 = QLabel("속도(BPM)"); l2.setObjectName("stat_desc")
        v2.addWidget(self.lbl_stat_bpm); v2.addWidget(l2)

        # Total Notes
        v3 = QVBoxLayout()
        self.lbl_stat_notes = QLabel("0")
        self.lbl_stat_notes.setObjectName("stat_value")
        l3 = QLabel("노트 수"); l3.setObjectName("stat_desc")
        v3.addWidget(self.lbl_stat_notes); v3.addWidget(l3)

        # Range / Octave Fit
        v4 = QVBoxLayout()
        self.lbl_stat_range = QLabel("A0 — C8")
        self.lbl_stat_range.setObjectName("stat_value")
        self.btn_octave_fit = QPushButton("옥타브 맞춤")
        self.btn_octave_fit.setStyleSheet("font-size: 10px; padding: 2px 6px; background-color: #2D3748;")
        self.btn_octave_fit.clicked.connect(self._apply_octave_fit)
        v4.addWidget(self.lbl_stat_range)
        v4.addWidget(self.btn_octave_fit)

        stats_layout.addLayout(v1)
        stats_layout.addLayout(v2)
        stats_layout.addLayout(v3)
        stats_layout.addLayout(v4)
        info_layout.addLayout(stats_layout, 3)

        layout.addWidget(self.info_card)

        # Piano Roll Widget
        self.piano_roll = PianoRollWidget(self)
        self.piano_roll.seek_requested.connect(self._handle_seek)
        layout.addWidget(self.piano_roll, 2)

        # 88-Key Interactive Virtual Piano Widget
        self.virtual_piano = VirtualPianoWidget(self.mapper, self)
        self.virtual_piano.key_clicked.connect(self._handle_piano_key_clicked)
        layout.addWidget(self.virtual_piano)

        # Controls & Hand Assignment Card
        self.controls_card = QFrame()
        self.controls_card.setObjectName("card")
        ctrl_layout = QVBoxLayout(self.controls_card)
        ctrl_layout.setContentsMargins(16, 12, 16, 12)
        ctrl_layout.setSpacing(10)

        # Row 1: Hand selection + Speed + Transpose
        row1 = QHBoxLayout()

        # Hands
        self.chk_rh = QCheckBox("오른손 (RH)")
        self.chk_rh.setChecked(True)
        self.chk_rh.toggled.connect(self._toggle_hands)

        self.chk_lh = QCheckBox("왼손 (LH)")
        self.chk_lh.setChecked(True)
        self.chk_lh.toggled.connect(self._toggle_hands)

        row1.addWidget(self.chk_rh)
        row1.addWidget(self.chk_lh)
        row1.addSpacing(20)

        # Speed slider
        row1.addWidget(QLabel("배속:"))
        self.slider_speed = QSlider(Qt.Horizontal)
        self.slider_speed.setRange(25, 200)  # 0.25x to 2.00x
        self.slider_speed.setValue(100)
        self.slider_speed.setFixedWidth(110)
        self.lbl_speed_val = QLabel("1.00×")
        self.lbl_speed_val.setFixedWidth(42)
        self.slider_speed.valueChanged.connect(self._on_speed_slider_changed)
        row1.addWidget(self.slider_speed)
        row1.addWidget(self.lbl_speed_val)
        row1.addSpacing(20)

        # Transpose slider
        row1.addWidget(QLabel("조옮김:"))
        self.slider_transpose = QSlider(Qt.Horizontal)
        self.slider_transpose.setRange(-12, 12)
        self.slider_transpose.setValue(0)
        self.slider_transpose.setFixedWidth(110)
        self.lbl_transpose_val = QLabel("0")
        self.lbl_transpose_val.setFixedWidth(24)
        self.slider_transpose.valueChanged.connect(self._on_transpose_changed)
        row1.addWidget(self.slider_transpose)
        row1.addWidget(self.lbl_transpose_val)

        btn_reset_transpose = QPushButton("초기화")
        btn_reset_transpose.setStyleSheet("font-size: 11px; padding: 2px 6px;")
        btn_reset_transpose.clicked.connect(lambda: self.slider_transpose.setValue(0))
        row1.addWidget(btn_reset_transpose)

        row1.addStretch()
        ctrl_layout.addLayout(row1)

        # Row 2: Big Play / Pause / Stop Buttons
        row2 = QHBoxLayout()

        self.btn_main_play = QPushButton("▶ 재생 (F6)")
        self.btn_main_play.setObjectName("primary_btn")
        self.btn_main_play.setFixedHeight(42)
        self.btn_main_play.clicked.connect(self._handle_play_button_click)

        self.btn_main_stop = QPushButton("■ 정지 (F8)")
        self.btn_main_stop.setFixedHeight(42)
        self.btn_main_stop.clicked.connect(self._handle_stop_button_click)

        self.lbl_guide_text = QLabel("로블록스 피아노에 앉은 후 F6을 누르세요 (3초 카운트다운).")
        self.lbl_guide_text.setStyleSheet("color: #94A3B8; font-size: 12px; margin-left: 12px;")

        row2.addWidget(self.btn_main_play, 2)
        row2.addWidget(self.btn_main_stop, 1)
        row2.addWidget(self.lbl_guide_text, 3)

        ctrl_layout.addLayout(row2)
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

        self.overlay.show()

    def _connect_signals(self) -> None:
        self.sig_state_changed.connect(self._on_playback_state_changed)
        self.sig_progress.connect(self._on_playback_progress)
        self.sig_countdown.connect(self._on_playback_countdown)
        self.sig_notes_played.connect(self._on_notes_played)

    def _apply_theme(self) -> None:
        self.setStyleSheet(get_stylesheet(self.config.theme))

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
            file_path = urls[0].toLocalFile()
            self.load_score_file(file_path)

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

    def load_score_file(self, file_path: str) -> None:
        if not os.path.isfile(file_path):
            QMessageBox.warning(self, "파일 오류", f"파일을 찾을 수 없습니다: {file_path}")
            return

        ext = os.path.splitext(file_path)[1].lower()
        try:
            timeline = None
            if ext in (".mid", ".midi"):
                timeline = self.midi_importer.import_score(file_path)
            elif ext in (".musicxml", ".xml", ".mxl"):
                timeline = self.musicxml_importer.import_score(file_path)
            elif ext in (".txt", ".num", ".jianpu"):
                timeline = self.numeric_importer.import_score(file_path)
            elif ext in (".png", ".jpg", ".jpeg", ".bmp", ".tiff"):
                timeline = self.image_importer.import_score(file_path)
            elif ext in (".pdf",):
                timeline = self.pdf_importer.import_score(file_path)
            else:
                QMessageBox.warning(self, "지원하지 않는 포맷", f"지원하지 않는 파일 확장자입니다: {ext}")
                return

            if timeline:
                self._set_loaded_timeline(timeline)

        except Exception as e:
            QMessageBox.critical(self, "불러오기 오류", f"악보를 불러오는데 실패했습니다:\n{str(e)}")

    def _set_loaded_timeline(self, timeline: MusicTimeline) -> None:
        self.timeline = timeline
        self.scheduler.set_timeline(timeline)

        # Update UI stats
        self.lbl_song_title.setText(timeline.title)
        self.overlay.set_song_title(timeline.title)

        dur_min, dur_sec = divmod(int(timeline.duration), 60)
        self.lbl_stat_dur.setText(f"{dur_min:02d}:{dur_sec:02d}")
        self.lbl_stat_bpm.setText(f"{int(timeline.initial_bpm)}")
        self.lbl_stat_notes.setText(f"{timeline.total_notes:,}")

        # Range check
        analysis = RangeProcessor.analyze_range(timeline)
        if analysis.out_of_range_count > 0:
            self.lbl_stat_range.setText(f"Out: {analysis.out_of_range_count}")
            self.lbl_stat_range.setStyleSheet("color: #EF4444; font-weight: bold;")
            self.btn_octave_fit.show()
        else:
            self.lbl_stat_range.setText("A0 — C8 (OK)")
            self.lbl_stat_range.setStyleSheet("color: #10B981; font-weight: bold;")
            self.btn_octave_fit.hide()

        # Update Piano Roll & Virtual Piano
        self.piano_roll.set_timeline(timeline)
        self.view_stack.setCurrentIndex(1)  # Switch to Player View

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
        self.lbl_stat_range.setText("A0 — C8 (Fitted)")
        self.lbl_stat_range.setStyleSheet("color: #10B981; font-weight: bold;")
        self.btn_octave_fit.hide()
        QMessageBox.information(
            self, "옥타브 맞춤 적용됨",
            f"로블록스 88건반 범위에 맞게 {modified}개의 노트를 성공적으로 조정했습니다."
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
        self.lbl_transpose_val.setText(f"{semitones:+d}" if semitones != 0 else "0")
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
            self.lbl_status_badge.setStyleSheet("background-color: #059669; color: #D1FAE5; font-weight: bold; padding: 4px 10px; border-radius: 6px;")
            self.btn_main_play.setText("⏸ 일시정지 (F7)")
        elif state == PlaybackState.PAUSED:
            self.lbl_status_badge.setText("일시정지됨")
            self.lbl_status_badge.setStyleSheet("background-color: #DC2626; color: #FEE2E2; font-weight: bold; padding: 4px 10px; border-radius: 6px;")
            self.btn_main_play.setText("▶ 계속 (F7)")
        elif state == PlaybackState.COUNTDOWN:
            self.lbl_status_badge.setText("카운트다운")
            self.lbl_status_badge.setStyleSheet("background-color: #D97706; color: #FEF3C7; font-weight: bold; padding: 4px 10px; border-radius: 6px;")
        else:
            self.lbl_status_badge.setText("준비됨")
            self.lbl_status_badge.setStyleSheet("background-color: #2D3748; color: #E2E8F0; font-weight: bold; padding: 4px 10px; border-radius: 6px;")
            self.btn_main_play.setText("▶ 재생 (F6)")

    @Slot(float, float)
    def _on_playback_progress(self, current: float, total: float) -> None:
        self.piano_roll.set_playhead_time(current)
        self.overlay.set_progress(current, total)

    @Slot(int)
    def _on_playback_countdown(self, sec: int) -> None:
        self.overlay.set_countdown(sec)

    @Slot(list)
    def _on_notes_played(self, notes: List[NoteEvent]) -> None:
        pitches = {n.pitch for n in notes}
        self.virtual_piano.set_active_pitches(pitches)
        self._active_notes_timer.start()

    def _clear_active_piano_keys(self) -> None:
        self._active_notes_timer.stop()
        self.virtual_piano.set_active_pitches(set())

    def _update_roblox_focus_status(self) -> None:
        if self.target_window.is_roblox_foreground():
            self.lbl_roblox_status.setText("● 로블록스 창 활성화됨")
            self.lbl_roblox_status.setStyleSheet("color: #10B981; font-size: 12px; margin-left: 10px;")
        else:
            self.lbl_roblox_status.setText("○ 로블록스 창 비활성화됨")
            self.lbl_roblox_status.setStyleSheet("color: #94A3B8; font-size: 12px; margin-left: 10px;")

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
    def closeEvent(self, event) -> None:
        """Ensures all keys are released, scheduler stopped, and hotkeys unhooked."""
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
