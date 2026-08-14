from PySide6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QLabel, QTextEdit, QPushButton, QMessageBox
from PySide6.QtCore import Qt

from src.importers.mml_importer import MmlImporter

class MmlDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("MML 붙여넣기")
        self.setMinimumSize(500, 400)
        self.setStyleSheet("""
            QDialog {
                background-color: #0F172A;
                color: #F8FAFC;
            }
            QLabel {
                color: #CBD5E1;
            }
            QTextEdit {
                background-color: #1E293B;
                border: 1px solid #334155;
                color: #F8FAFC;
                border-radius: 4px;
                font-family: Consolas, monospace;
            }
            QPushButton {
                background-color: #3B82F6;
                color: white;
                border: none;
                border-radius: 4px;
                padding: 8px 16px;
            }
            QPushButton:hover {
                background-color: #2563EB;
            }
            QPushButton#btnCancel {
                background-color: #475569;
            }
            QPushButton#btnCancel:hover {
                background-color: #334155;
            }
        """)

        layout = QVBoxLayout(self)

        lbl_title = QLabel("MML 코드 입력")
        lbl_title.setStyleSheet("font-size: 16px; font-weight: bold; color: #FFFFFF;")
        layout.addWidget(lbl_title)

        self.text_edit = QTextEdit()
        self.text_edit.setPlaceholderText("MML@T131V15L16...")
        self.text_edit.textChanged.connect(self._on_text_changed)
        layout.addWidget(self.text_edit)

        self.lbl_info = QLabel("트랙: 0 | 템포: 120 BPM")
        self.lbl_info.setStyleSheet("color: #94A3B8; font-size: 12px;")
        layout.addWidget(self.lbl_info)

        btn_layout = QHBoxLayout()
        btn_layout.addStretch()

        self.btn_cancel = QPushButton("취소")
        self.btn_cancel.setObjectName("btnCancel")
        self.btn_cancel.clicked.connect(self.reject)

        self.btn_add = QPushButton("변환 후 라이브러리에 추가")
        self.btn_add.clicked.connect(self.accept)
        self.btn_add.setEnabled(False)

        btn_layout.addWidget(self.btn_cancel)
        btn_layout.addWidget(self.btn_add)
        layout.addLayout(btn_layout)

        self.importer = MmlImporter()
        self.mml_code = ""

    def _on_text_changed(self):
        text = self.text_edit.toPlainText().strip()
        self.mml_code = text
        if text:
            try:
                meta = self.importer.extract_metadata(text)
                self.lbl_info.setText(f"트랙: {meta.get('tracks', 0)} | 템포: {meta.get('tempo', 120)} BPM")
                self.btn_add.setEnabled(True)
            except Exception as e:
                self.lbl_info.setText("잘못된 MML 형식입니다.")
                self.btn_add.setEnabled(False)
        else:
            self.lbl_info.setText("트랙: 0 | 템포: 120 BPM")
            self.btn_add.setEnabled(False)

    def get_mml_code(self) -> str:
        return self.mml_code
