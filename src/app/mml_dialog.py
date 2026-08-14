from PySide6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QLabel, QTextEdit, QPushButton, QMessageBox, QLineEdit
from PySide6.QtCore import Qt

from src.importers.mml_importer import MmlImporter, MmlParseError

class MmlDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("MML 붙여넣기")
        self.setMinimumSize(600, 450)
        
        # Windows 11 style, Segoe UI, no emojis
        self.setStyleSheet("""
            QDialog {
                background-color: #1c1c1c;
                color: #ffffff;
                font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif;
            }
            QLabel {
                color: #dcdcdc;
                font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif;
            }
            QLineEdit, QTextEdit {
                background-color: #2d2d2d;
                border: 1px solid #3f3f3f;
                color: #ffffff;
                border-radius: 6px;
                padding: 6px;
                font-family: Consolas, monospace;
            }
            QLineEdit:focus, QTextEdit:focus {
                border: 1px solid #0078d4;
            }
            QPushButton {
                background-color: #333333;
                color: #ffffff;
                border: 1px solid #3f3f3f;
                border-radius: 6px;
                padding: 6px 16px;
                font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif;
            }
            QPushButton:hover {
                background-color: #3f3f3f;
            }
            QPushButton#primaryBtn {
                background-color: #0078d4;
                color: white;
                border: none;
            }
            QPushButton#primaryBtn:hover {
                background-color: #2b88d8;
            }
            QPushButton:disabled {
                background-color: #1e1e1e;
                color: #666666;
                border: 1px solid #2d2d2d;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setSpacing(12)
        layout.setContentsMargins(20, 20, 20, 20)

        # Title Input
        title_layout = QHBoxLayout()
        lbl_title = QLabel("악보 제목:")
        self.input_title = QLineEdit()
        self.input_title.setPlaceholderText("새로운 MML 악보")
        title_layout.addWidget(lbl_title)
        title_layout.addWidget(self.input_title)
        layout.addLayout(title_layout)

        # Editor
        lbl_editor = QLabel("MML 코드 입력:")
        layout.addWidget(lbl_editor)

        self.text_edit = QTextEdit()
        self.text_edit.setPlaceholderText("MML@T131V15L16...")
        self.text_edit.textChanged.connect(self._on_text_changed)
        layout.addWidget(self.text_edit)

        # Info Label (Errors or Stats)
        self.lbl_info = QLabel("트랙: 0 | 템포: 120 BPM")
        self.lbl_info.setStyleSheet("color: #a0a0a0; font-size: 13px;")
        layout.addWidget(self.lbl_info)

        # Buttons
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()

        self.btn_cancel = QPushButton("취소")
        self.btn_cancel.clicked.connect(self.reject)

        self.btn_add = QPushButton("라이브러리에 추가")
        self.btn_add.clicked.connect(self.accept)
        self.btn_add.setEnabled(False)
        
        self.btn_add_and_play = QPushButton("추가 후 재생")
        self.btn_add_and_play.setObjectName("primaryBtn")
        self.btn_add_and_play.clicked.connect(self._accept_and_play)
        self.btn_add_and_play.setEnabled(False)

        btn_layout.addWidget(self.btn_cancel)
        btn_layout.addWidget(self.btn_add)
        btn_layout.addWidget(self.btn_add_and_play)
        layout.addLayout(btn_layout)

        self.importer = MmlImporter()
        self.mml_code = ""
        self._should_play = False

    def _on_text_changed(self):
        text = self.text_edit.toPlainText().strip()
        self.mml_code = text
        if text:
            try:
                # Perform a full parse check without saving to disk to validate properly
                # We do this by parsing it and dumping it to a dummy in-memory mid object
                import mido
                dummy_mid = mido.MidiFile(ticks_per_beat=480)
                
                # To do this safely without writing file, we can just call extract_metadata which we will update
                # Or we can just try to run convert_to_midi to os.devnull
                import os
                self.importer.convert_to_midi(text, os.devnull)
                
                meta = self.importer.extract_metadata(text)
                self.lbl_info.setStyleSheet("color: #a0a0a0; font-size: 13px;")
                dur_s = int(meta.get('duration', 0))
                mins, secs = divmod(dur_s, 60)
                dur_str = f"{mins:02d}:{secs:02d}"
                notes = meta.get('total_notes', 0)
                self.lbl_info.setText(f"정상 MML | 트랙: {meta.get('tracks', 0)} | 템포: {meta.get('tempo', 120)} BPM | 길이: {dur_str} | 노트: {notes:,}")
                
                self.btn_add.setEnabled(True)
                self.btn_add_and_play.setEnabled(True)
                
            except MmlParseError as e:
                self.lbl_info.setStyleSheet("color: #ff9999; font-size: 13px;")
                self.lbl_info.setText(str(e))
                self.btn_add.setEnabled(False)
                self.btn_add_and_play.setEnabled(False)
            except Exception as e:
                self.lbl_info.setStyleSheet("color: #ff9999; font-size: 13px;")
                self.lbl_info.setText(f"MML 파싱 오류: {str(e)}")
                self.btn_add.setEnabled(False)
                self.btn_add_and_play.setEnabled(False)
        else:
            self.lbl_info.setStyleSheet("color: #a0a0a0; font-size: 13px;")
            self.lbl_info.setText("트랙: 0 | 템포: 120 BPM")
            self.btn_add.setEnabled(False)
            self.btn_add_and_play.setEnabled(False)

    def _accept_and_play(self):
        self._should_play = True
        self.accept()

    def get_mml_code(self) -> str:
        return self.mml_code
        
    def get_title(self) -> str:
        t = self.input_title.text().strip()
        return t if t else "새로운 MML 악보"
        
    def should_play(self) -> bool:
        return self._should_play
