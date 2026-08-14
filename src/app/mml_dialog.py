import os
from typing import Optional, Dict, Any, Tuple
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QTextEdit, QPushButton,
    QMessageBox, QLineEdit, QFileDialog, QFrame, QToolButton
)
from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QTextCursor, QTextOption

from src.services.mml_service import MmlConversionService
from src.library.manager import LibraryManager
from src.library.models import ScoreItem
from src.music.timeline import MusicTimeline

class MmlDialog(QDialog):
    """
    High-end MML Import Dialog matching Apple / Google / Microsoft Windows 11 aesthetics.
    Integrates with MmlConversionService for robust parsing and MIDI export.
    """
    def __init__(self, manager: LibraryManager, current_folder_id=None, parent=None):
        super().__init__(parent)
        self.manager = manager
        self.current_folder_id = current_folder_id
        self.service = MmlConversionService()
        
        self.setWindowTitle("MML 가져오기")
        self.setMinimumSize(680, 520)
        self.resize(720, 560)
        
        self.created_score_item: ScoreItem = None
        self.created_timeline: MusicTimeline = None
        self._should_play = False
        self._last_error_pos = None

        self._setup_ui()
        self._on_text_changed()

    def _setup_ui(self):
        self.setStyleSheet("""
            QDialog {
                background-color: #0D1117;
                color: #F2F5F8;
                font-family: 'Segoe UI Variable', 'Segoe UI', -apple-system, sans-serif;
            }
            QLabel {
                color: #A8B1BF;
                font-size: 13px;
                background-color: transparent;
                border: none;
            }
            QLabel#sectionHeader {
                color: #F2F5F8;
                font-size: 14px;
                font-weight: 600;
            }
            QLineEdit {
                background-color: #151A22;
                border: 1px solid #28313D;
                color: #F2F5F8;
                border-radius: 6px;
                padding: 8px 12px;
                font-size: 13px;
            }
            QLineEdit:focus {
                border: 1px solid #4C82F7;
                background-color: #1B222D;
            }
            QTextEdit {
                background-color: #151A22;
                border: 1px solid #28313D;
                color: #E6EDF3;
                border-radius: 6px;
                padding: 10px;
                font-family: 'Cascadia Mono', 'Consolas', monospace;
                font-size: 13px;
                line-height: 1.4;
            }
            QTextEdit:focus {
                border: 1px solid #4C82F7;
                background-color: #171E28;
            }
            QPushButton {
                background-color: #21262D;
                color: #F2F5F8;
                border: 1px solid #30363D;
                border-radius: 6px;
                padding: 8px 16px;
                font-size: 13px;
                font-weight: 500;
                min-height: 20px;
            }
            QPushButton:hover {
                background-color: #30363D;
                border-color: #8B949E;
            }
            QPushButton:pressed {
                background-color: #161B22;
            }
            QPushButton#primaryBtn {
                background-color: #4C82F7;
                color: #FFFFFF;
                border: none;
                font-weight: 600;
            }
            QPushButton#primaryBtn:hover {
                background-color: #3B72E6;
            }
            QPushButton#primaryBtn:pressed {
                background-color: #2D62D5;
            }
            QPushButton:disabled {
                background-color: #161B22;
                color: #484F58;
                border: 1px solid #21262D;
            }
            QFrame#badgeContainer {
                background-color: #151A22;
                border: 1px solid #28313D;
                border-radius: 6px;
                padding: 6px 12px;
            }
            QToolButton {
                background-color: transparent;
                border: none;
                color: #A8B1BF;
                padding: 4px 8px;
                font-size: 12px;
            }
            QToolButton:hover {
                color: #F2F5F8;
                background-color: #21262D;
                border-radius: 4px;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.setSpacing(14)

        # Title Row
        title_box = QVBoxLayout()
        title_box.setSpacing(6)
        lbl_title = QLabel("곡 제목", self)
        lbl_title.setObjectName("sectionHeader")
        self.input_title = QLineEdit(self)
        self.input_title.setPlaceholderText("새로운 MML 악보")
        title_box.addWidget(lbl_title)
        title_box.addWidget(self.input_title)
        layout.addLayout(title_box)

        # Editor Header Row (Label + Wrap Toggle)
        editor_hdr = QHBoxLayout()
        lbl_editor = QLabel("MML 코드", self)
        lbl_editor.setObjectName("sectionHeader")
        editor_hdr.addWidget(lbl_editor)
        editor_hdr.addStretch()

        self.btn_wrap = QToolButton(self)
        self.btn_wrap.setText("줄 바꿈: 켬")
        self.btn_wrap.setCheckable(True)
        self.btn_wrap.setChecked(True)
        self.btn_wrap.clicked.connect(self._toggle_wrap)
        editor_hdr.addWidget(self.btn_wrap)
        layout.addLayout(editor_hdr)

        # Editor
        self.text_edit = QTextEdit(self)
        self.text_edit.setPlaceholderText("MML@T150V15L16... 코드를 붙여넣으세요.")
        self.text_edit.setLineWrapMode(QTextEdit.WidgetWidth)
        self.text_edit.textChanged.connect(self._on_text_changed)
        layout.addWidget(self.text_edit, 1)

        # Metadata Badge Bar
        self.badge_container = QFrame(self)
        self.badge_container.setObjectName("badgeContainer")
        badge_layout = QHBoxLayout(self.badge_container)
        badge_layout.setContentsMargins(10, 6, 10, 6)
        badge_layout.setSpacing(12)

        self.lbl_status_icon = QLabel("●", self)
        self.lbl_status_icon.setStyleSheet("color: #758195; font-size: 14px;")
        self.lbl_meta_text = QLabel("MML 코드를 입력해 주세요.", self)
        self.lbl_meta_text.setStyleSheet("color: #A8B1BF; font-size: 13px;")

        badge_layout.addWidget(self.lbl_status_icon)
        badge_layout.addWidget(self.lbl_meta_text, 1)
        layout.addWidget(self.badge_container)

        # Buttons Row
        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(8)

        self.btn_cancel = QPushButton("취소", self)
        self.btn_cancel.clicked.connect(self.reject)

        self.btn_save_midi = QPushButton("MIDI로 저장...", self)
        self.btn_save_midi.clicked.connect(self._on_save_midi)
        self.btn_save_midi.setEnabled(False)

        self.btn_add = QPushButton("라이브러리에 추가", self)
        self.btn_add.clicked.connect(self._on_add_to_library)
        self.btn_add.setEnabled(False)

        self.btn_add_and_play = QPushButton("추가 후 재생", self)
        self.btn_add_and_play.setObjectName("primaryBtn")
        self.btn_add_and_play.clicked.connect(self._on_add_and_play)
        self.btn_add_and_play.setEnabled(False)

        btn_layout.addWidget(self.btn_cancel)
        btn_layout.addStretch()
        btn_layout.addWidget(self.btn_save_midi)
        btn_layout.addWidget(self.btn_add)
        btn_layout.addWidget(self.btn_add_and_play)
        layout.addLayout(btn_layout)

    def _toggle_wrap(self):
        if self.btn_wrap.isChecked():
            self.text_edit.setLineWrapMode(QTextEdit.WidgetWidth)
            self.btn_wrap.setText("줄 바꿈: 켬")
        else:
            self.text_edit.setLineWrapMode(QTextEdit.NoWrap)
            self.btn_wrap.setText("줄 바꿈: 끔")

    def _on_text_changed(self):
        text = self.text_edit.toPlainText().strip()
        if not text:
            self.lbl_status_icon.setStyleSheet("color: #758195;")
            self.lbl_meta_text.setText("MML 코드를 입력해 주세요.")
            self.lbl_meta_text.setStyleSheet("color: #758195;")
            self.btn_save_midi.setEnabled(False)
            self.btn_add.setEnabled(False)
            self.btn_add_and_play.setEnabled(False)
            self._last_error_pos = None
            return

        is_valid, meta, error_msg, err_pos = self.service.validate_and_analyze(text)
        self._last_error_pos = err_pos

        if is_valid and meta:
            self.lbl_status_icon.setStyleSheet("color: #3FB950;") # Green dot
            dur_s = int(meta.get('duration', 0))
            mins, secs = divmod(dur_s, 60)
            notes = meta.get('total_notes', 0)
            tempo = meta.get('tempo', 120)
            tracks = meta.get('tracks', 1)
            
            self.lbl_meta_text.setText(
                f"정상 MML  ·  {tracks}개 트랙  ·  {tempo} BPM  ·  {mins:02d}:{secs:02d}  ·  {notes:,} 노트"
            )
            self.lbl_meta_text.setStyleSheet("color: #F2F5F8; font-weight: 500;")
            
            self.btn_save_midi.setEnabled(True)
            self.btn_add.setEnabled(True)
            self.btn_add_and_play.setEnabled(True)
        else:
            self.lbl_status_icon.setStyleSheet("color: #F85149;") # Red dot
            self.lbl_meta_text.setText(f"오류: {error_msg}")
            self.lbl_meta_text.setStyleSheet("color: #FF7B72;")
            
            self.btn_save_midi.setEnabled(False)
            self.btn_add.setEnabled(False)
            self.btn_add_and_play.setEnabled(False)

    def _on_save_midi(self):
        text = self.text_edit.toPlainText().strip()
        if not text:
            return
        
        default_name = self.service.sanitize_title(self.input_title.text(), "score") + ".mid"
        file_path, _ = QFileDialog.getSaveFileName(
            self, "MIDI 파일로 저장", default_name, "MIDI Files (*.mid *.midi)"
        )
        if file_path:
            try:
                stats = self.service.export_to_file(text, file_path)
                QMessageBox.information(
                    self, "저장 완료",
                    f"MIDI 파일이 성공적으로 저장되었습니다.\n\n"
                    f"경로: {file_path}\n"
                    f"크기: {stats['file_size']:,} 바이트\n"
                    f"음표 수: {stats['note_count']:,} 개"
                )
            except Exception as e:
                QMessageBox.critical(self, "저장 오류", f"MIDI 저장 중 오류가 발생했습니다:\n{e}")

    def _on_add_to_library(self):
        if self._perform_import():
            self.accept()

    def _on_add_and_play(self):
        if self._perform_import():
            self._should_play = True
            self.accept()

    def _perform_import(self) -> bool:
        text = self.text_edit.toPlainText().strip()
        if not text:
            return False
        
        title = self.input_title.text().strip()
        try:
            item, timeline, stats = self.service.import_to_library(
                mml_text=text,
                title=title,
                folder_id=self.current_folder_id,
                manager=self.manager
            )
            self.created_score_item = item
            self.created_timeline = timeline
            return True
        except Exception as e:
            QMessageBox.critical(self, "가져오기 오류", f"라이브러리 등록 중 오류가 발생했습니다:\n{e}")
            return False

    def should_play(self) -> bool:
        return self._should_play

    def get_created_score_item(self) -> Optional[ScoreItem]:
        return self.created_score_item

    def get_created_timeline(self) -> Optional[MusicTimeline]:
        return self.created_timeline
