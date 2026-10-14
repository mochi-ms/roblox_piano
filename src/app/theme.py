"""
Roblox Piano Player - shared Studio Precision theme tokens and Qt stylesheet.
"""

DARK_TOKENS = {
    "canvas": "#0B0C0E",
    "background": "#121316",
    "surface": "#181A1F",
    "control": "#22252C",
    "hover": "#2B2F38",
    "border": "#262932",
    "border_focus": "#3B82F6",
    "text": "#F3F4F6",
    "text_secondary": "#9CA3AF",
    "text_muted": "#6B7280",
    "accent": "#3B82F6",
    "accent_secondary": "#14B8A6",
    "success": "#10B981",
    "warning": "#F59E0B",
    "danger": "#EF4444",
}

LIGHT_TOKENS = {
    "canvas": "#F1F5F9",
    "background": "#FFFFFF",
    "surface": "#F8FAFC",
    "control": "#E2E8F0",
    "hover": "#CBD5E1",
    "border": "#E2E8F0",
    "border_focus": "#2563EB",
    "text": "#0F172A",
    "text_secondary": "#475569",
    "text_muted": "#94A3B8",
    "accent": "#2563EB",
    "accent_secondary": "#0D9488",
    "success": "#059669",
    "warning": "#D97706",
    "danger": "#DC2626",
}


def get_stylesheet(theme_name: str = "dark") -> str:
    tokens = LIGHT_TOKENS if theme_name.lower() == "light" else DARK_TOKENS
    t = tokens
    return f"""
QWidget {{
    background-color: {t["background"]};
    color: {t["text"]};
    font-family: "Malgun Gothic", "Segoe UI", sans-serif;
    font-size: 13px;
}}
QMainWindow, QDialog {{ background: {t["background"]}; }}
QToolTip {{
    background: {t["surface"]};
    color: {t["text"]};
    border: 1px solid {t["border"]};
    border-radius: 5px;
    padding: 5px 8px;
    font-size: 12px;
}}
QFrame#GlobalHeader, QFrame#trackHeader, QFrame#ControlDeck, QFrame#card {{
    background: {t["surface"]};
    border: 1px solid {t["border"]};
    border-radius: 8px;
}}
QLabel#appTitle {{ font-size: 15px; font-weight: 700; color: {t["text"]}; }}
QLabel#statusBadge {{
    min-height: 22px;
    padding: 0 8px;
    border: 1px solid {t["border"]};
    border-radius: 5px;
    background: {t["control"]};
    color: {t["text_secondary"]};
    font-size: 11px;
    font-weight: 600;
}}
QLabel#statusBadge[state="playing"] {{
    background: {t["success"]};
    border-color: {t["success"]};
    color: white;
}}
QLabel#statusBadge[state="paused"] {{
    background: {t["control"]};
    border-color: {t["warning"]};
    color: {t["warning"]};
}}
QLabel#statusBadge[state="countdown"] {{
    background: {t["control"]};
    border-color: {t["warning"]};
    color: {t["warning"]};
}}
QLabel#robloxStatus {{
    color: {t["text_muted"]};
    font-size: 11px;
}}
QLabel#robloxStatus[state="active"] {{ color: {t["success"]}; }}
QLabel#robloxStatus[state="waiting"] {{ color: {t["text_secondary"]}; }}
QPushButton {{
    min-height: 30px;
    padding: 0 11px;
    background: {t["control"]};
    color: {t["text"]};
    border: 1px solid {t["border"]};
    border-radius: 6px;
    font-weight: 500;
}}
QPushButton:hover {{ background: {t["hover"]}; }}
QPushButton:pressed {{ background: {t["surface"]}; }}
QPushButton:disabled {{ color: {t["text_muted"]}; background: {t["surface"]}; }}
QPushButton#primary_btn, QPushButton#primaryAction {{
    min-height: 32px;
    background: {t["accent"]};
    color: white;
    border-color: {t["accent"]};
    font-weight: 600;
}}
QPushButton#primary_btn:hover, QPushButton#primaryAction:hover {{ background: {t["border_focus"]}; }}
QPushButton#headerAction, QPushButton#headerToggle {{
    min-height: 30px;
    padding: 0 9px;
    background: transparent;
    border-color: transparent;
}}
QPushButton#headerAction:hover, QPushButton#headerToggle:hover {{
    background: {t["control"]};
    border-color: {t["border"]};
}}
QPushButton#headerToggle:checked {{
    color: {t["accent"]};
    background: {t["control"]};
    border-color: {t["accent"]};
}}
QPushButton#workspaceSegment {{
    min-height: 30px;
    padding: 0 12px;
    background: transparent;
    color: {t["text_secondary"]};
    border: 1px solid transparent;
    border-radius: 5px;
}}
QPushButton#workspaceSegment:hover {{ color: {t["text"]}; background: {t["control"]}; }}
QPushButton#workspaceSegment:checked {{
    background: {t["control"]};
    color: {t["text"]};
    border-color: {t["border"]};
    font-weight: 600;
}}
QPushButton#secondaryAction {{ background: {t["control"]}; }}
QPushButton#smallControl {{
    min-width: 26px;
    max-width: 26px;
    min-height: 26px;
    max-height: 26px;
    padding: 0;
    font-weight: 600;
}}
QCheckBox {{ spacing: 6px; color: {t["text_secondary"]}; }}
QCheckBox::indicator {{
    width: 14px;
    height: 14px;
    border: 1px solid {t["border"]};
    border-radius: 3px;
    background: {t["control"]};
}}
QCheckBox::indicator:checked {{ background: {t["accent"]}; border-color: {t["accent"]}; }}
QCheckBox#handToggle {{
    min-height: 26px;
    padding: 0 7px;
    border: 1px solid {t["border"]};
    border-radius: 5px;
    background: {t["control"]};
    color: {t["text_secondary"]};
    spacing: 0;
    font-size: 12px;
}}
QCheckBox#handToggle::indicator {{ width: 0; height: 0; border: none; }}
QCheckBox#handToggle:checked {{
    background: {t["surface"]};
    border-color: {t["accent"]};
    color: {t["text"]};
}}
QLabel#trackTitle {{ font-size: 16px; font-weight: 700; color: {t["text"]}; }}
QLabel#sectionHeading {{ font-size: 13px; font-weight: 600; color: {t["text"]}; }}
QLabel#filePathLabel {{ color: {t["text_secondary"]}; font-size: 12px; }}
QLabel#settingsStatus[state="success"] {{ color: {t["success"]}; }}
QLabel#settingsStatus[state="warning"] {{ color: {t["warning"]}; }}
QLabel#metricValue, QLabel#numericValue {{
    color: {t["text_secondary"]};
    font-family: "Cascadia Mono", "Consolas", monospace;
    font-size: 11px;
}}
QLabel#rangeValue[state="warning"] {{ color: {t["warning"]}; }}
QLabel#rangeValue[state="success"] {{ color: {t["success"]}; }}
QLabel#countdownBanner {{
    min-height: 28px;
    padding: 0 10px;
    background: {t["control"]};
    color: {t["warning"]};
    border: 1px solid {t["warning"]};
    border-radius: 6px;
    font-weight: 600;
}}
QFrame#drop_card, QFrame#localDropCard {{
    background: {t["surface"]};
    border: 1px dashed {t["border"]};
    border-radius: 8px;
}}
QFrame#localDropCard[dragging="true"] {{
    border-color: {t["accent"]};
}}
QFrame#drop_card:hover {{ border-color: {t["accent"]}; }}
QLabel#dropTitle {{ font-size: 17px; font-weight: 650; color: {t["text"]}; }}
QLabel#dropHint {{ color: {t["text_secondary"]}; font-size: 12px; }}
QLabel#guideText {{ color: {t["text_muted"]}; font-size: 11px; }}
QTabWidget::pane {{ border: none; }}
QLineEdit, QComboBox, QSpinBox, QDoubleSpinBox {{
    min-height: 30px;
    padding: 0 9px;
    background: {t["surface"]};
    color: {t["text"]};
    border: 1px solid {t["border"]};
    border-radius: 6px;
    selection-background-color: {t["accent"]};
}}
QLineEdit:focus, QComboBox:focus, QSpinBox:focus, QDoubleSpinBox:focus {{
    border-color: {t["border_focus"]};
}}
QComboBox::drop-down {{ border: none; width: 22px; }}
QSlider::groove:horizontal {{
    height: 4px;
    background: {t["control"]};
    border-radius: 2px;
}}
QSlider::sub-page:horizontal {{ background: {t["accent"]}; border-radius: 2px; }}
QSlider::handle:horizontal {{
    width: 12px;
    height: 12px;
    margin: -5px 0;
    border-radius: 6px;
    background: {t["text"]};
    border: 2px solid {t["accent"]};
}}
QProgressBar {{
    min-height: 6px;
    max-height: 6px;
    background: {t["control"]};
    border: none;
    border-radius: 3px;
}}
QProgressBar::chunk {{ background: {t["accent"]}; border-radius: 3px; }}
QStatusBar {{
    min-height: 27px;
    max-height: 28px;
    background: {t["background"]};
    border-top: 1px solid {t["border"]};
    color: {t["text_muted"]};
    font-size: 11px;
}}
QStatusBar::item {{ border: none; }}
QGroupBox {{
    margin-top: 13px;
    padding: 10px 12px 12px 12px;
    border: 1px solid {t["border"]};
    border-radius: 8px;
    color: {t["text"]};
    font-weight: 600;
}}
QGroupBox::title {{
    subcontrol-origin: margin;
    left: 11px;
    padding: 0 5px;
    color: {t["text_secondary"]};
}}
QScrollArea {{ border: none; background: transparent; }}
QScrollArea QWidget {{ background: transparent; }}
QScrollBar:vertical {{
    width: 8px;
    background: transparent;
    margin: 0;
}}
QScrollBar::handle:vertical {{
    min-height: 20px;
    background: {t["border"]};
    border-radius: 4px;
}}
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{ height: 0; }}
QMenu {{
    background: {t["surface"]};
    color: {t["text"]};
    border: 1px solid {t["border"]};
    padding: 4px;
}}
QMenu::item {{ padding: 6px 24px 6px 10px; border-radius: 4px; }}
QMenu::item:selected {{ background: {t["control"]}; }}
"""
