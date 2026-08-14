"""
Roblox Piano Player - Modern Styling & Themes (Dark & Light)
"""

DARK_THEME = """
QWidget {
    background-color: #121316;
    color: #F1F3F5;
    font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, 'Pretendard', sans-serif;
    font-size: 13px;
}

QToolTip {
    background-color: #252830;
    color: #FFFFFF;
    border: 1px solid #383D48;
    border-radius: 6px;
    padding: 6px 10px;
    font-size: 12px;
}

QFrame#card {
    background-color: #1A1D23;
    border: 1px solid #282C34;
    border-radius: 12px;
}

QFrame#drop_card {
    background-color: #16181D;
    border: 2px dashed #3A404D;
    border-radius: 16px;
}

QFrame#drop_card:hover {
    border-color: #4C8BF5;
    background-color: #1B1E26;
}

QLabel#title_label {
    font-size: 22px;
    font-weight: 700;
    color: #FFFFFF;
}

QLabel#subtitle_label {
    font-size: 13px;
    color: #8C93A0;
}

QLabel#section_heading {
    font-size: 12px;
    font-weight: 600;
    color: #7E8694;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

QLabel#stat_value {
    font-size: 16px;
    font-weight: 600;
    color: #E2E6EC;
}

QLabel#stat_desc {
    font-size: 11px;
    color: #7E8694;
}

QPushButton {
    background-color: #242832;
    border: 1px solid #343A46;
    color: #E6E9EE;
    border-radius: 8px;
    padding: 8px 16px;
    font-weight: 500;
}

QPushButton:hover {
    background-color: #2D3340;
    border-color: #464D5D;
}

QPushButton:pressed {
    background-color: #1E222A;
}

QPushButton:disabled {
    background-color: #16181D;
    border-color: #22262E;
    color: #525966;
}

QPushButton#primary_btn {
    background-color: #3875E8;
    border: none;
    color: #FFFFFF;
    font-weight: 600;
    font-size: 14px;
    border-radius: 8px;
    padding: 10px 20px;
}

QPushButton#primary_btn:hover {
    background-color: #4C85F0;
}

QPushButton#primary_btn:pressed {
    background-color: #2A63D4;
}

QPushButton#accent_toggle {
    background-color: #222630;
    border: 1px solid #353B47;
    color: #9AA1AF;
    border-radius: 6px;
    font-weight: 600;
    font-size: 12px;
    padding: 6px 12px;
}

QPushButton#accent_toggle:checked {
    background-color: #2B4C8C;
    border: 1px solid #4878D9;
    color: #FFFFFF;
}

QSlider::groove:horizontal {
    height: 6px;
    background: #252932;
    border-radius: 3px;
}

QSlider::sub-page:horizontal {
    background: #3B7BF6;
    border-radius: 3px;
}

QSlider::handle:horizontal {
    background: #FFFFFF;
    border: 2px solid #3B7BF6;
    width: 16px;
    margin-top: -5px;
    margin-bottom: -5px;
    border-radius: 8px;
}

QSlider::handle:horizontal:hover {
    background: #E8F0FE;
    transform: scale(1.1);
}

QProgressBar {
    background-color: #20242D;
    border-radius: 4px;
    height: 6px;
    text-align: center;
}

QProgressBar::chunk {
    background-color: #3B7BF6;
    border-radius: 4px;
}

QTabWidget::pane {
    border: 1px solid #282C34;
    background-color: #1A1D23;
    border-radius: 8px;
}

QTabBar::tab {
    background-color: transparent;
    color: #8C93A0;
    padding: 8px 16px;
    font-weight: 600;
    border-bottom: 2px solid transparent;
}

QTabBar::tab:selected {
    color: #FFFFFF;
    border-bottom: 2px solid #3B7BF6;
}

QTabBar::tab:hover:!selected {
    color: #C0C6D0;
}

QLineEdit, QComboBox, QSpinBox, QDoubleSpinBox {
    background-color: #1B1E25;
    border: 1px solid #323744;
    border-radius: 6px;
    padding: 6px 10px;
    color: #FFFFFF;
}

QLineEdit:focus, QComboBox:focus, QSpinBox:focus {
    border: 1px solid #3B7BF6;
}

QComboBox::drop-down {
    border: none;
    width: 24px;
}

QScrollBar:vertical {
    border: none;
    background: #15171C;
    width: 8px;
    border-radius: 4px;
}

QScrollBar::handle:vertical {
    background: #2E333E;
    min-height: 20px;
    border-radius: 4px;
}

QScrollBar::handle:vertical:hover {
    background: #404756;
}

QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {
    height: 0px;
}
"""

LIGHT_THEME = """
QWidget {
    background-color: #F8F9FA;
    color: #1A1D20;
    font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, 'Pretendard', sans-serif;
    font-size: 13px;
}

QToolTip {
    background-color: #FFFFFF;
    color: #1A1D20;
    border: 1px solid #DFE2E6;
    border-radius: 6px;
    padding: 6px 10px;
    font-size: 12px;
}

QFrame#card {
    background-color: #FFFFFF;
    border: 1px solid #E6E8EC;
    border-radius: 12px;
}

QFrame#drop_card {
    background-color: #F3F5F7;
    border: 2px dashed #C8CDD5;
    border-radius: 16px;
}

QFrame#drop_card:hover {
    border-color: #3875E8;
    background-color: #EDF2FC;
}

QLabel#title_label {
    font-size: 22px;
    font-weight: 700;
    color: #111315;
}

QLabel#subtitle_label {
    font-size: 13px;
    color: #6C757D;
}

QLabel#section_heading {
    font-size: 12px;
    font-weight: 600;
    color: #6C757D;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

QLabel#stat_value {
    font-size: 16px;
    font-weight: 600;
    color: #212529;
}

QLabel#stat_desc {
    font-size: 11px;
    color: #6C757D;
}

QPushButton {
    background-color: #FFFFFF;
    border: 1px solid #CED4DA;
    color: #212529;
    border-radius: 8px;
    padding: 8px 16px;
    font-weight: 500;
}

QPushButton:hover {
    background-color: #E9ECEF;
    border-color: #ADB5BD;
}

QPushButton:pressed {
    background-color: #DEE2E6;
}

QPushButton#primary_btn {
    background-color: #2B66D9;
    border: none;
    color: #FFFFFF;
    font-weight: 600;
    font-size: 14px;
    border-radius: 8px;
    padding: 10px 20px;
}

QPushButton#primary_btn:hover {
    background-color: #3875E8;
}

QPushButton#primary_btn:pressed {
    background-color: #1E50B8;
}

QPushButton#accent_toggle {
    background-color: #F1F3F5;
    border: 1px solid #CED4DA;
    color: #495057;
    border-radius: 6px;
    font-weight: 600;
    font-size: 12px;
    padding: 6px 12px;
}

QPushButton#accent_toggle:checked {
    background-color: #E7EFFF;
    border: 1px solid #3875E8;
    color: #1D54C2;
}

QSlider::groove:horizontal {
    height: 6px;
    background: #E2E6EA;
    border-radius: 3px;
}

QSlider::sub-page:horizontal {
    background: #3B7BF6;
    border-radius: 3px;
}

QSlider::handle:horizontal {
    background: #FFFFFF;
    border: 2px solid #3B7BF6;
    width: 16px;
    margin-top: -5px;
    margin-bottom: -5px;
    border-radius: 8px;
}

QProgressBar {
    background-color: #E9ECEF;
    border-radius: 4px;
    height: 6px;
    text-align: center;
}

QProgressBar::chunk {
    background-color: #3B7BF6;
    border-radius: 4px;
}
"""


def get_stylesheet(theme_name: str = "dark") -> str:
    if theme_name.lower() == "light":
        return LIGHT_THEME
    return DARK_THEME
