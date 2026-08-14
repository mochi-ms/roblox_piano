"""
Roblox Piano Player - Settings & Configuration Dialog
"""
from typing import Optional, Callable
from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTabWidget, QWidget, QLabel,
    QComboBox, QSpinBox, QDoubleSpinBox, QCheckBox, QPushButton,
    QFileDialog, QLineEdit, QFormLayout, QGroupBox
)
from src.utils.config import AppConfig, ConfigManager
from src.omr.audiveris_backend import AudiverisBackend


class SettingsDialog(QDialog):
    settings_saved = Signal(AppConfig)

    def __init__(self, config_manager: ConfigManager, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.config_mgr = config_manager
        self.config: AppConfig = config_manager.config

        self.setWindowTitle("설정 • 로블록스 자동 피아노 연주기")
        self.setMinimumWidth(480)
        self.setModal(True)
        self._setup_ui()
        self._load_values()

    def _setup_ui(self) -> None:
        layout = QVBoxLayout(self)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(16)

        self.tabs = QTabWidget(self)

        # Tab 1: General & Playback
        tab_general = QWidget()
        form_general = QFormLayout(tab_general)
        form_general.setContentsMargins(12, 12, 12, 12)
        form_general.setSpacing(12)

        self.combo_theme = QComboBox()
        self.combo_theme.addItems(["dark", "light"])

        self.spin_countdown = QSpinBox()
        self.spin_countdown.setRange(0, 10)
        self.spin_countdown.setSuffix(" sec")

        self.spin_hold_dur = QDoubleSpinBox()
        self.spin_hold_dur.setRange(5.0, 200.0)
        self.spin_hold_dur.setSingleStep(5.0)
        self.spin_hold_dur.setSuffix(" ms")

        self.combo_conflict = QComboBox()
        self.combo_conflict.addItems(["MICRO_ARPEGGIO", "SKIP_CONFLICTED", "WARN_ONLY"])

        self.spin_conflict_delay = QDoubleSpinBox()
        self.spin_conflict_delay.setRange(2.0, 50.0)
        self.spin_conflict_delay.setSuffix(" ms")

        self.chk_target_safety = QCheckBox("로블록스가 비활성화 상태일 때 입력 방지")
        self.combo_focus_loss = QComboBox()
        self.combo_focus_loss.addItems(["PAUSE", "STOP", "CONTINUE"])

        self.chk_dry_run = QCheckBox("테스트 모드 (실제 키보드 입력을 보내지 않음)")

        form_general.addRow("테마:", self.combo_theme)
        form_general.addRow("시작 전 대기 시간(초):", self.spin_countdown)
        form_general.addRow("기본 건반 누름 지속시간(ms):", self.spin_hold_dur)
        form_general.addRow("동일 건반 충돌 정책:", self.combo_conflict)
        form_general.addRow("충돌 시 분산화음(아르페지오) 지연:", self.spin_conflict_delay)
        form_general.addRow("로블록스 포커스 안전성:", self.chk_target_safety)
        form_general.addRow("포커스를 잃었을 때:", self.combo_focus_loss)
        form_general.addRow("테스트 모드:", self.chk_dry_run)

        # Tab 2: Hotkeys & Overlay
        tab_hotkeys = QWidget()
        form_hotkeys = QFormLayout(tab_hotkeys)
        form_hotkeys.setContentsMargins(12, 12, 12, 12)
        form_hotkeys.setSpacing(12)

        self.edit_hk_play = QLineEdit()
        self.edit_hk_pause = QLineEdit()
        self.edit_hk_stop = QLineEdit()
        self.edit_hk_overlay = QLineEdit()

        self.spin_opacity = QDoubleSpinBox()
        self.spin_opacity.setRange(0.3, 1.0)
        self.spin_opacity.setSingleStep(0.05)

        self.chk_click_through = QCheckBox("클릭 무시 (클릭이 오버레이를 통과하여 로블록스에 적용됨)")

        form_hotkeys.addRow("재생 단축키:", self.edit_hk_play)
        form_hotkeys.addRow("일시정지/계속 단축키:", self.edit_hk_pause)
        form_hotkeys.addRow("긴급 정지 단축키:", self.edit_hk_stop)
        form_hotkeys.addRow("오버레이(HUD) 표시 단축키:", self.edit_hk_overlay)
        form_hotkeys.addRow("오버레이 투명도:", self.spin_opacity)
        form_hotkeys.addRow("오버레이 클릭 통과:", self.chk_click_through)

        # Tab 3: OMR (Score Recognition)
        tab_omr = QWidget()
        layout_omr = QVBoxLayout(tab_omr)
        layout_omr.setContentsMargins(12, 12, 12, 12)
        layout_omr.setSpacing(12)

        self.lbl_omr_status = QLabel()
        audiveris = AudiverisBackend()
        if audiveris.is_available():
            self.lbl_omr_status.setText("● Audiveris OMR이 시스템에 설치되어 있습니다.")
            self.lbl_omr_status.setStyleSheet("color: #10B981; font-weight: bold;")
        else:
            self.lbl_omr_status.setText("○ Audiveris OMR 경로가 감지되지 않았습니다.\n(이미지 악보 인식을 제외한 MIDI, MusicXML 등은 정상 작동합니다)")
            self.lbl_omr_status.setStyleSheet("color: #F59E0B;")

        layout_omr.addWidget(self.lbl_omr_status)

        path_layout = QHBoxLayout()
        self.edit_audiveris_path = QLineEdit()
        self.edit_audiveris_path.setPlaceholderText("audiveris.bat 또는 실행 파일 경로를 지정하세요...")
        btn_browse_omr = QPushButton("찾아보기...")
        btn_browse_omr.clicked.connect(self._browse_audiveris)
        path_layout.addWidget(self.edit_audiveris_path)
        path_layout.addWidget(btn_browse_omr)

        layout_omr.addLayout(path_layout)
        layout_omr.addStretch()

        self.tabs.addTab(tab_general, "재생 및 안전성 설정")
        self.tabs.addTab(tab_hotkeys, "단축키 및 오버레이 설정")
        self.tabs.addTab(tab_omr, "악보 인식 (OMR) 설정")

        # Tab 4: Pedal
        tab_pedal = QWidget()
        form_pedal = QFormLayout(tab_pedal)
        form_pedal.setContentsMargins(12, 12, 12, 12)
        form_pedal.setSpacing(12)

        self.chk_pedal_enabled = QCheckBox("페달(Sustain CC64) 화면 자동 클릭 활성화")
        self.spin_pedal_x = QDoubleSpinBox()
        self.spin_pedal_x.setRange(0.0, 1.0)
        self.spin_pedal_x.setSingleStep(0.05)
        self.spin_pedal_y = QDoubleSpinBox()
        self.spin_pedal_y.setRange(0.0, 1.0)
        self.spin_pedal_y.setSingleStep(0.05)
        
        self.combo_pedal_mode = QComboBox()
        self.combo_pedal_mode.addItems(["toggle", "hold"])

        form_pedal.addRow("", self.chk_pedal_enabled)
        form_pedal.addRow("화면 X 비율 (0.0~1.0):", self.spin_pedal_x)
        form_pedal.addRow("화면 Y 비율 (0.0~1.0):", self.spin_pedal_y)
        form_pedal.addRow("동작 방식:", self.combo_pedal_mode)

        self.tabs.addTab(tab_pedal, "페달 설정")

        layout.addWidget(self.tabs)

        # Action buttons (Save, Cancel)
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        btn_cancel = QPushButton("취소")
        btn_cancel.clicked.connect(self.reject)
        btn_save = QPushButton("설정 저장")
        btn_save.setObjectName("primary_btn")
        btn_save.clicked.connect(self._save_values)

        btn_layout.addWidget(btn_cancel)
        btn_layout.addWidget(btn_save)
        layout.addLayout(btn_layout)

    def _browse_audiveris(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self, "Audiveris 실행 파일 선택", "", "실행 파일 (*.exe *.bat);;모든 파일 (*.*)"
        )
        if path:
            self.edit_audiveris_path.setText(path)

    def _load_values(self) -> None:
        c = self.config
        self.combo_theme.setCurrentText(c.theme)
        self.spin_countdown.setValue(c.countdown_seconds)
        self.spin_hold_dur.setValue(c.hold_duration_ms)
        self.combo_conflict.setCurrentText(c.conflict_policy)
        self.spin_conflict_delay.setValue(c.conflict_delay_ms)
        self.chk_target_safety.setChecked(c.target_window_safety)
        self.combo_focus_loss.setCurrentText(c.focus_loss_policy)
        self.chk_dry_run.setChecked(c.dry_run_mode)

        self.edit_hk_play.setText(c.hotkey_play)
        self.edit_hk_pause.setText(c.hotkey_pause)
        self.edit_hk_stop.setText(c.hotkey_stop)
        self.edit_hk_overlay.setText(c.hotkey_overlay)
        self.spin_opacity.setValue(c.overlay_opacity)
        self.chk_click_through.setChecked(c.overlay_click_through)
        self.edit_audiveris_path.setText(c.audiveris_path)

        self.chk_pedal_enabled.setChecked(c.pedal_enabled)
        self.spin_pedal_x.setValue(c.pedal_x_ratio)
        self.spin_pedal_y.setValue(c.pedal_y_ratio)
        self.combo_pedal_mode.setCurrentText(c.pedal_mode)

    def _save_values(self) -> None:
        c = self.config
        c.theme = self.combo_theme.currentText()
        c.countdown_seconds = self.spin_countdown.value()
        c.hold_duration_ms = self.spin_hold_dur.value()
        c.conflict_policy = self.combo_conflict.currentText()
        c.conflict_delay_ms = self.spin_conflict_delay.value()
        c.target_window_safety = self.chk_target_safety.isChecked()
        c.focus_loss_policy = self.combo_focus_loss.currentText()
        c.dry_run_mode = self.chk_dry_run.isChecked()

        c.hotkey_play = self.edit_hk_play.text().strip() or "F6"
        c.hotkey_pause = self.edit_hk_pause.text().strip() or "F7"
        c.hotkey_stop = self.edit_hk_stop.text().strip() or "F8"
        c.hotkey_overlay = self.edit_hk_overlay.text().strip() or "F4"
        c.overlay_opacity = self.spin_opacity.value()
        c.overlay_click_through = self.chk_click_through.isChecked()
        c.audiveris_path = self.edit_audiveris_path.text().strip()

        c.pedal_enabled = self.chk_pedal_enabled.isChecked()
        c.pedal_x_ratio = self.spin_pedal_x.value()
        c.pedal_y_ratio = self.spin_pedal_y.value()
        c.pedal_mode = self.combo_pedal_mode.currentText()

        self.config_mgr.save_config(c)
        self.settings_saved.emit(c)
        self.accept()
