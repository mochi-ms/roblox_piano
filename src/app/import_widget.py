from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QLineEdit, QProgressBar, QMessageBox, QFileDialog, QGroupBox
)

class ImportWidget(QWidget):
    """
    Widget for importing scores via YouTube URL or Local Video.
    (Phase B/E integration)
    """
    # Signal emitted when a new video task should start
    # arg1: source_type ("YOUTUBE" or "LOCAL_VIDEO")
    # arg2: source_path (URL or filepath)
    import_requested = Signal(str, str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(20)

        # Title
        lbl_title = QLabel("비디오 / YouTube 악보 자동 생성")
        lbl_title.setStyleSheet("font-size: 18px; font-weight: bold;")
        layout.addWidget(lbl_title)

        # YouTube Group
        yt_group = QGroupBox("YouTube 링크로 가져오기")
        yt_layout = QVBoxLayout(yt_group)
        self.input_url = QLineEdit()
        self.input_url.setPlaceholderText("https://www.youtube.com/watch?v=...")
        
        btn_yt_import = QPushButton("YouTube 분석 시작")
        btn_yt_import.setObjectName("primary_btn")
        btn_yt_import.clicked.connect(self._on_yt_import)
        
        yt_layout.addWidget(self.input_url)
        yt_layout.addWidget(btn_yt_import, 0, Qt.AlignRight)
        layout.addWidget(yt_group)

        # Local Video Group
        local_group = QGroupBox("로컬 비디오 파일로 가져오기")
        local_layout = QVBoxLayout(local_group)
        self.lbl_local_file = QLabel("선택된 파일 없음")
        self.lbl_local_file.setStyleSheet("color: #94A3B8;")
        
        btn_browse = QPushButton("비디오 파일 찾기 (.mp4, .mkv)")
        btn_browse.clicked.connect(self._on_browse_local)
        
        btn_local_import = QPushButton("로컬 비디오 분석 시작")
        btn_local_import.setObjectName("primary_btn")
        btn_local_import.clicked.connect(self._on_local_import)
        
        local_h = QHBoxLayout()
        local_h.addWidget(btn_browse)
        local_h.addWidget(self.lbl_local_file, 1)
        
        local_layout.addLayout(local_h)
        local_layout.addWidget(btn_local_import, 0, Qt.AlignRight)
        layout.addWidget(local_group)

        # Progress Area
        self.progress_group = QGroupBox("분석 진행 상태")
        prog_layout = QVBoxLayout(self.progress_group)
        
        self.lbl_status = QLabel("대기 중...")
        self.progress_bar = QProgressBar()
        self.progress_bar.setRange(0, 100)
        self.progress_bar.setValue(0)
        
        self.btn_cancel = QPushButton("취소")
        self.btn_cancel.setEnabled(False)
        self.btn_cancel.clicked.connect(self._on_cancel)
        
        prog_layout.addWidget(self.lbl_status)
        prog_layout.addWidget(self.progress_bar)
        prog_layout.addWidget(self.btn_cancel, 0, Qt.AlignRight)
        
        layout.addWidget(self.progress_group)
        self.progress_group.hide()  # Hide initially

        layout.addStretch()
        
        self.local_filepath = ""

    def _on_yt_import(self):
        url = self.input_url.text().strip()
        if not url:
            QMessageBox.warning(self, "입력 오류", "YouTube URL을 입력하세요.")
            return
        self._start_task("YOUTUBE", url)

    def _on_browse_local(self):
        path, _ = QFileDialog.getOpenFileName(self, "Select Video", "", "Video Files (*.mp4 *.mkv *.avi *.mov)")
        if path:
            self.local_filepath = path
            self.lbl_local_file.setText(path)

    def _on_local_import(self):
        if not self.local_filepath:
            QMessageBox.warning(self, "선택 오류", "먼저 로컬 비디오 파일을 선택하세요.")
            return
        self._start_task("LOCAL_VIDEO", self.local_filepath)

    def _start_task(self, source_type: str, path: str):
        self.progress_group.show()
        self.lbl_status.setText("준비 중...")
        self.progress_bar.setValue(0)
        self.btn_cancel.setEnabled(True)
        self.import_requested.emit(source_type, path)

    def update_progress(self, percent: int, status_text: str):
        self.progress_bar.setValue(percent)
        self.lbl_status.setText(status_text)

    def task_finished(self, success: bool, message: str):
        self.btn_cancel.setEnabled(False)
        if success:
            self.lbl_status.setText(f"완료! {message}")
            self.progress_bar.setValue(100)
        else:
            self.lbl_status.setText(f"오류 발생: {message}")

    def _on_cancel(self):
        self.lbl_status.setText("취소 중...")
        self.btn_cancel.setEnabled(False)
        # TODO: emit cancel signal
