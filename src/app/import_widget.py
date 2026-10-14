from os.path import basename, splitext

from PySide6.QtCore import QEvent, Qt, Signal
from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QLineEdit, QProgressBar, QMessageBox, QFileDialog, QFrame, QSizePolicy
)


class ImportWidget(QWidget):
    """YouTube and local-video import workflow with non-blocking progress."""

    import_requested = Signal(str, str)
    score_open_requested = Signal(object)

    VIDEO_EXTENSIONS = {".mp4", ".mkv", ".avi", ".mov"}

    def __init__(self, parent=None):
        super().__init__(parent)
        self.local_filepath = ""
        self.completed_item = None
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(16, 14, 16, 14)
        layout.setSpacing(10)

        title = QLabel("악보 가져오기")
        title.setObjectName("trackTitle")
        subtitle = QLabel("YouTube 연주 영상이나 PC의 동영상에서 악보를 추출합니다.")
        subtitle.setObjectName("dropHint")
        layout.addWidget(title)
        layout.addWidget(subtitle)

        source_row = QHBoxLayout()
        source_row.setSpacing(10)

        self.yt_card = QFrame(self)
        self.yt_card.setObjectName("card")
        yt_layout = QVBoxLayout(self.yt_card)
        yt_layout.setContentsMargins(14, 12, 14, 12)
        yt_layout.setSpacing(8)
        yt_title = QLabel("YouTube")
        yt_title.setObjectName("sectionHeading")
        yt_hint = QLabel("피아노 또는 신시사이저 연주 영상 주소")
        yt_hint.setObjectName("dropHint")
        yt_row = QHBoxLayout()
        yt_row.setSpacing(7)
        self.input_url = QLineEdit(self.yt_card)
        self.input_url.setPlaceholderText("YouTube URL")
        self.input_url.setMinimumWidth(120)
        self.input_url.setClearButtonEnabled(True)
        self.btn_yt_import = QPushButton("가져오기", self.yt_card)
        self.btn_yt_import.setObjectName("primary_btn")
        self.btn_yt_import.clicked.connect(self._on_yt_import)
        yt_row.addWidget(self.input_url, 1)
        yt_row.addWidget(self.btn_yt_import)
        yt_layout.addWidget(yt_title)
        yt_layout.addWidget(yt_hint)
        yt_layout.addStretch(1)
        yt_layout.addLayout(yt_row)
        source_row.addWidget(self.yt_card, 1)

        self.local_card = QFrame(self)
        self.local_card.setObjectName("localDropCard")
        self.local_card.setAcceptDrops(True)
        self.local_card.installEventFilter(self)
        local_layout = QVBoxLayout(self.local_card)
        local_layout.setContentsMargins(14, 12, 14, 12)
        local_layout.setSpacing(8)
        local_title = QLabel("내 PC의 동영상")
        local_title.setObjectName("sectionHeading")
        local_hint = QLabel("MP4, MKV, AVI, MOV · 파일을 여기에 놓아도 됩니다")
        local_hint.setObjectName("dropHint")
        file_row = QHBoxLayout()
        file_row.setSpacing(7)
        self.lbl_local_file = QLabel("선택된 파일 없음")
        self.lbl_local_file.setObjectName("filePathLabel")
        self.lbl_local_file.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Preferred)
        self.lbl_local_file.setMinimumWidth(0)
        self.btn_browse_local = QPushButton("파일 선택", self.local_card)
        self.btn_browse_local.clicked.connect(self._on_browse_local)
        file_row.addWidget(self.lbl_local_file, 1)
        file_row.addWidget(self.btn_browse_local)
        self.btn_local_import = QPushButton("분석 시작", self.local_card)
        self.btn_local_import.setObjectName("primary_btn")
        self.btn_local_import.clicked.connect(self._on_local_import)
        local_layout.addWidget(local_title)
        local_layout.addWidget(local_hint)
        local_layout.addStretch(1)
        local_layout.addLayout(file_row)
        local_layout.addWidget(self.btn_local_import, 0, Qt.AlignRight)
        source_row.addWidget(self.local_card, 1)
        layout.addLayout(source_row)

        self.progress_card = QFrame(self)
        self.progress_card.setObjectName("card")
        progress_layout = QVBoxLayout(self.progress_card)
        progress_layout.setContentsMargins(14, 12, 14, 12)
        progress_layout.setSpacing(7)
        heading_row = QHBoxLayout()
        self.lbl_progress_title = QLabel("분석 작업")
        self.lbl_progress_title.setObjectName("sectionHeading")
        self.lbl_status = QLabel("대기 중")
        self.lbl_status.setObjectName("dropHint")
        heading_row.addWidget(self.lbl_progress_title)
        heading_row.addStretch(1)
        heading_row.addWidget(self.lbl_status)
        self.progress_bar = QProgressBar(self.progress_card)
        self.progress_bar.setRange(0, 100)
        self.progress_bar.setValue(0)
        self.progress_bar.setTextVisible(False)
        action_row = QHBoxLayout()
        action_row.addStretch(1)
        self.btn_open_result = QPushButton("플레이어에서 열기")
        self.btn_open_result.setObjectName("primary_btn")
        self.btn_open_result.hide()
        self.btn_open_result.clicked.connect(self._open_result)
        self.btn_cancel = QPushButton("취소")
        self.btn_cancel.setEnabled(False)
        self.btn_cancel.clicked.connect(self._on_cancel)
        action_row.addWidget(self.btn_open_result)
        action_row.addWidget(self.btn_cancel)
        progress_layout.addLayout(heading_row)
        progress_layout.addWidget(self.progress_bar)
        progress_layout.addLayout(action_row)
        layout.addWidget(self.progress_card)
        self.progress_card.hide()
        layout.addStretch(1)

    def _set_local_file(self, path: str) -> None:
        self.local_filepath = path
        self.lbl_local_file.setText(basename(path))
        self.lbl_local_file.setToolTip(path)

    def eventFilter(self, watched, event):
        if watched is self.local_card:
            if event.type() == QEvent.Type.DragEnter:
                urls = event.mimeData().urls() if event.mimeData().hasUrls() else []
                if any(
                    splitext(url.toLocalFile())[1].lower() in self.VIDEO_EXTENSIONS
                    for url in urls if url.isLocalFile()
                ):
                    self.local_card.setProperty("dragging", True)
                    self.local_card.style().unpolish(self.local_card)
                    self.local_card.style().polish(self.local_card)
                    event.acceptProposedAction()
                    return True
            elif event.type() == QEvent.Type.DragLeave:
                self.local_card.setProperty("dragging", False)
                self.local_card.style().unpolish(self.local_card)
                self.local_card.style().polish(self.local_card)
            elif event.type() == QEvent.Type.Drop:
                self.local_card.setProperty("dragging", False)
                for url in event.mimeData().urls():
                    if not url.isLocalFile():
                        continue
                    path = url.toLocalFile()
                    if splitext(path)[1].lower() in self.VIDEO_EXTENSIONS:
                        self._set_local_file(path)
                        event.acceptProposedAction()
                        return True
        return super().eventFilter(watched, event)

    def _on_yt_import(self):
        url = self.input_url.text().strip()
        if not url:
            QMessageBox.warning(self, "입력 오류", "YouTube URL을 입력하세요.")
            return
        self._start_task("YOUTUBE", url)

    def _on_browse_local(self):
        path, _ = QFileDialog.getOpenFileName(
            self, "동영상 파일 선택", "",
            "Video Files (*.mp4 *.mkv *.avi *.mov)"
        )
        if path:
            self._set_local_file(path)

    def _on_local_import(self):
        if not self.local_filepath:
            QMessageBox.warning(self, "선택 오류", "먼저 로컬 동영상 파일을 선택하세요.")
            return
        self._start_task("LOCAL_VIDEO", self.local_filepath)

    def _start_task(self, source_type: str, path: str):
        self.completed_item = None
        self.btn_open_result.hide()
        self.progress_card.show()
        self.lbl_status.setText("준비 중")
        self.progress_bar.setValue(0)
        self.btn_cancel.setEnabled(True)
        self.import_requested.emit(source_type, path)

    def update_progress(self, percent: int, status_text: str):
        self.progress_bar.setValue(max(0, min(100, percent)))
        self.lbl_status.setText(status_text)

    def task_finished(self, success: bool, message: str, item=None):
        self.btn_cancel.setEnabled(False)
        if success:
            self.lbl_status.setText(message)
            self.progress_bar.setValue(100)
            self.completed_item = item
            self.btn_open_result.setVisible(item is not None)
        else:
            self.lbl_status.setText(f"오류 · {message}")
            self.btn_open_result.hide()

    def _open_result(self):
        if self.completed_item is not None:
            self.score_open_requested.emit(self.completed_item)

    def _on_cancel(self):
        self.lbl_status.setText("취소 중")
        self.btn_cancel.setEnabled(False)
