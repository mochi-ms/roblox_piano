"""
Roblox Piano Player - Settings & Configuration Dialog
"""
from typing import Optional, Callable
from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QWidget, QLabel,
    QComboBox, QSpinBox, QDoubleSpinBox, QCheckBox, QPushButton,
    QFileDialog, QLineEdit, QFormLayout, QGroupBox, QScrollArea
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
        self.setMinimumSize(600, 520)
        self.resize(680, 680)

        outer = QVBoxLayout(self)
        outer.setContentsMargins(16, 14, 16, 12)
        outer.setSpacing(10)

        title = QLabel("환경 설정")
        title.setObjectName("trackTitle")
        outer.addWidget(title)

        scroll = QScrollArea(self)
        scroll.setWidgetResizable(True)
        content = QWidget()
        content_layout = QVBoxLayout(content)
        content_layout.setContentsMargins(2, 2, 8, 2)
        content_layout.setSpacing(8)

        def make_form(group):
            form = QFormLayout(group)
            form.setContentsMargins(8, 8, 8, 8)
            form.setHorizontalSpacing(14)
            form.setVerticalSpacing(8)
            form.setFieldGrowthPolicy(QFormLayout.AllNonFixedFieldsGrow)
            return form

        group_general = QGroupBox("연주 및 안전성")
        form_general = make_form(group_general)

        self.combo_theme = QComboBox()
        self.combo_theme.addItem("다크", "dark")
        self.combo_theme.addItem("라이트", "light")

        self.spin_countdown = QSpinBox()
        self.spin_countdown.setRange(0, 10)
        self.spin_countdown.setSuffix("초")

        self.spin_hold_dur = QDoubleSpinBox()
        self.spin_hold_dur.setRange(5.0, 200.0)
        self.spin_hold_dur.setSingleStep(5.0)
        self.spin_hold_dur.setSuffix(" ms")

        self.combo_conflict = QComboBox()
        self.combo_conflict.addItem("동시 입력을 짧게 분산", "MICRO_ARPEGGIO")
        self.combo_conflict.addItem("충돌한 음표 건너뛰기", "SKIP_CONFLICTED")
        self.combo_conflict.addItem("경고만 표시", "WARN_ONLY")

        self.spin_conflict_delay = QDoubleSpinBox()
        self.spin_conflict_delay.setRange(2.0, 50.0)
        self.spin_conflict_delay.setSuffix(" ms")

        self.chk_target_safety = QCheckBox("Roblox가 비활성화되면 키 입력 방지")
        self.combo_focus_loss = QComboBox()
        self.combo_focus_loss.addItem("일시 정지", "PAUSE")
        self.combo_focus_loss.addItem("연주 중지", "STOP")
        self.combo_focus_loss.addItem("계속 연주", "CONTINUE")
        self.chk_dry_run = QCheckBox("테스트 모드 · 실제 키 입력을 보내지 않음")

        form_general.addRow("테마", self.combo_theme)
        form_general.addRow("연주 전 카운트다운", self.spin_countdown)
        form_general.addRow("기본 건반 누름 시간", self.spin_hold_dur)
        form_general.addRow("건반 충돌 처리", self.combo_conflict)
        form_general.addRow("분산 입력 간격", self.spin_conflict_delay)
        form_general.addRow("", self.chk_target_safety)
        form_general.addRow("포커스를 잃었을 때", self.combo_focus_loss)
        form_general.addRow("", self.chk_dry_run)
        content_layout.addWidget(group_general)

        group_hotkeys = QGroupBox("단축키 및 오버레이")
        form_hotkeys = make_form(group_hotkeys)
        self.edit_hk_play = QLineEdit()
        self.edit_hk_pause = QLineEdit()
        self.edit_hk_stop = QLineEdit()
        self.edit_hk_overlay = QLineEdit()
        self.spin_opacity = QDoubleSpinBox()
        self.spin_opacity.setRange(0.3, 1.0)
        self.spin_opacity.setSingleStep(0.05)
        self.chk_click_through = QCheckBox("오버레이 클릭을 통과시켜 Roblox에 전달")
        form_hotkeys.addRow("재생", self.edit_hk_play)
        form_hotkeys.addRow("일시 정지 / 계속", self.edit_hk_pause)
        form_hotkeys.addRow("정지", self.edit_hk_stop)
        form_hotkeys.addRow("오버레이 표시", self.edit_hk_overlay)
        form_hotkeys.addRow("오버레이 투명도", self.spin_opacity)
        form_hotkeys.addRow("", self.chk_click_through)
        content_layout.addWidget(group_hotkeys)

        group_omr = QGroupBox("악보 인식 도구")
        omr_layout = QVBoxLayout(group_omr)
        omr_layout.setContentsMargins(10, 12, 10, 10)
        omr_layout.setSpacing(8)
        self.lbl_omr_status = QLabel()
        self.lbl_omr_status.setObjectName("settingsStatus")
        audiveris = AudiverisBackend()
        if audiveris.is_available():
            self.lbl_omr_status.setText("Audiveris 사용 가능")
            self.lbl_omr_status.setProperty("state", "success")
        else:
            self.lbl_omr_status.setText(
                "Audiveris 경로를 찾지 못했습니다. 이미지와 PDF 인식에 필요합니다."
            )
            self.lbl_omr_status.setProperty("state", "warning")
        omr_layout.addWidget(self.lbl_omr_status)
        path_row = QHBoxLayout()
        self.edit_audiveris_path = QLineEdit()
        self.edit_audiveris_path.setPlaceholderText("audiveris.bat 또는 실행 파일 경로")
        btn_browse_omr = QPushButton("찾아보기")
        btn_browse_omr.clicked.connect(self._browse_audiveris)
        path_row.addWidget(self.edit_audiveris_path, 1)
        path_row.addWidget(btn_browse_omr)
        omr_layout.addLayout(path_row)
        content_layout.addWidget(group_omr)

        group_pedal = QGroupBox("페달")
        form_pedal = make_form(group_pedal)
        self.chk_pedal_enabled = QCheckBox("Sustain 페달 자동 입력 사용")
        self.spin_pedal_x = QDoubleSpinBox()
        self.spin_pedal_x.setRange(0.0, 1.0)
        self.spin_pedal_x.setSingleStep(0.05)
        self.spin_pedal_y = QDoubleSpinBox()
        self.spin_pedal_y.setRange(0.0, 1.0)
        self.spin_pedal_y.setSingleStep(0.05)
        self.combo_pedal_mode = QComboBox()
        self.combo_pedal_mode.addItem("켜기 / 끄기 전환", "toggle")
        self.combo_pedal_mode.addItem("누르고 있는 동안 유지", "hold")
        form_pedal.addRow("", self.chk_pedal_enabled)
        form_pedal.addRow("화면 가로 위치 비율", self.spin_pedal_x)
        form_pedal.addRow("화면 세로 위치 비율", self.spin_pedal_y)
        form_pedal.addRow("페달 입력 방식", self.combo_pedal_mode)
        content_layout.addWidget(group_pedal)
        content_layout.addStretch(1)

        scroll.setWidget(content)
        outer.addWidget(scroll, 1)

        button_row = QHBoxLayout()
        button_row.addStretch(1)
        btn_cancel = QPushButton("취소")
        btn_cancel.clicked.connect(self.reject)
        btn_save = QPushButton("설정 저장")
        btn_save.setObjectName("primary_btn")
        btn_save.clicked.connect(self._save_values)
        button_row.addWidget(btn_cancel)
        button_row.addWidget(btn_save)
        outer.addLayout(button_row)

    def _browse_audiveris(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self, "Audiveris 실행 파일 선택", "", "실행 파일 (*.exe *.bat);;모든 파일 (*.*)"
        )
        if path:
            self.edit_audiveris_path.setText(path)

    def _load_values(self) -> None:
        c = self.config
        self.combo_theme.setCurrentIndex(self.combo_theme.findData(c.theme))
        self.spin_countdown.setValue(c.countdown_seconds)
        self.spin_hold_dur.setValue(c.hold_duration_ms)
        self.combo_conflict.setCurrentIndex(self.combo_conflict.findData(c.conflict_policy))
        self.spin_conflict_delay.setValue(c.conflict_delay_ms)
        self.chk_target_safety.setChecked(c.target_window_safety)
        self.combo_focus_loss.setCurrentIndex(self.combo_focus_loss.findData(c.focus_loss_policy))
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
        self.combo_pedal_mode.setCurrentIndex(self.combo_pedal_mode.findData(c.pedal_mode))

    def _save_values(self) -> None:
        c = self.config
        c.theme = self.combo_theme.currentData() or self.combo_theme.currentText()
        c.countdown_seconds = self.spin_countdown.value()
        c.hold_duration_ms = self.spin_hold_dur.value()
        c.conflict_policy = self.combo_conflict.currentData() or self.combo_conflict.currentText()
        c.conflict_delay_ms = self.spin_conflict_delay.value()
        c.target_window_safety = self.chk_target_safety.isChecked()
        c.focus_loss_policy = self.combo_focus_loss.currentData() or self.combo_focus_loss.currentText()
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
        c.pedal_mode = self.combo_pedal_mode.currentData() or self.combo_pedal_mode.currentText()

        self.config_mgr.save_config(c)
        self.settings_saved.emit(c)
        self.accept()
