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

        self.setWindowTitle("Settings • Roblox Piano Player")
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

        self.chk_target_safety = QCheckBox("Prevent typing when Roblox is not in focus")
        self.combo_focus_loss = QComboBox()
        self.combo_focus_loss.addItems(["PAUSE", "STOP", "CONTINUE"])

        self.chk_dry_run = QCheckBox("Dry Run Mode (Simulate without sending real keystrokes)")

        form_general.addRow("Theme:", self.combo_theme)
        form_general.addRow("Countdown Before Play:", self.spin_countdown)
        form_general.addRow("Key Hold Duration:", self.spin_hold_dur)
        form_general.addRow("Key Conflict Policy (e.g. q/Q):", self.combo_conflict)
        form_general.addRow("Conflict Arpeggio Delay:", self.spin_conflict_delay)
        form_general.addRow("Roblox Focus Safety:", self.chk_target_safety)
        form_general.addRow("On Focus Lost:", self.combo_focus_loss)
        form_general.addRow("Testing Mode:", self.chk_dry_run)

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

        self.chk_click_through = QCheckBox("Enable Click-Through (Clicks pass to Roblox)")

        form_hotkeys.addRow("Play Hotkey:", self.edit_hk_play)
        form_hotkeys.addRow("Pause/Resume Hotkey:", self.edit_hk_pause)
        form_hotkeys.addRow("Emergency Stop Hotkey:", self.edit_hk_stop)
        form_hotkeys.addRow("Toggle HUD Hotkey:", self.edit_hk_overlay)
        form_hotkeys.addRow("HUD Opacity:", self.spin_opacity)
        form_hotkeys.addRow("HUD Click-Through:", self.chk_click_through)

        # Tab 3: OMR (Score Recognition)
        tab_omr = QWidget()
        layout_omr = QVBoxLayout(tab_omr)
        layout_omr.setContentsMargins(12, 12, 12, 12)
        layout_omr.setSpacing(12)

        self.lbl_omr_status = QLabel()
        audiveris = AudiverisBackend()
        if audiveris.is_available():
            self.lbl_omr_status.setText("● Audiveris OMR is installed and available.")
            self.lbl_omr_status.setStyleSheet("color: #10B981; font-weight: bold;")
        else:
            self.lbl_omr_status.setText("○ Audiveris OMR is not detected.\n(MIDI and MusicXML work without Audiveris)")
            self.lbl_omr_status.setStyleSheet("color: #F59E0B;")

        layout_omr.addWidget(self.lbl_omr_status)

        path_layout = QHBoxLayout()
        self.edit_audiveris_path = QLineEdit()
        self.edit_audiveris_path.setPlaceholderText("Path to audiveris.bat or executable...")
        btn_browse_omr = QPushButton("Browse...")
        btn_browse_omr.clicked.connect(self._browse_audiveris)
        path_layout.addWidget(self.edit_audiveris_path)
        path_layout.addWidget(btn_browse_omr)

        layout_omr.addLayout(path_layout)
        layout_omr.addStretch()

        self.tabs.addTab(tab_general, "Playback & Safety")
        self.tabs.addTab(tab_hotkeys, "Hotkeys & HUD")
        self.tabs.addTab(tab_omr, "OMR (Sheet Scanner)")

        layout.addWidget(self.tabs)

        # Action buttons (Save, Cancel)
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        btn_cancel = QPushButton("Cancel")
        btn_cancel.clicked.connect(self.reject)
        btn_save = QPushButton("Save Settings")
        btn_save.setObjectName("primary_btn")
        btn_save.clicked.connect(self._save_values)

        btn_layout.addWidget(btn_cancel)
        btn_layout.addWidget(btn_save)
        layout.addLayout(btn_layout)

    def _browse_audiveris(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self, "Select Audiveris Executable", "", "Executables (*.exe *.bat);;All Files (*.*)"
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

        self.config_mgr.save_config(c)
        self.settings_saved.emit(c)
        self.accept()
