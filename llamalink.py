"""
LlamaLink v0.1.0 - GUI Frontend for llama.cpp
A simple, versatile chat interface for local LLMs via llama-server.
"""

import sys, os, subprocess, json, signal, threading, time, glob, struct

APP_NAME = "LlamaLink"
APP_VERSION = "0.1.0"

def _bootstrap():
    """Auto-install dependencies before imports."""
    required = {"PyQt6": "PyQt6", "requests": "requests"}
    import importlib
    for mod, pkg in required.items():
        try:
            importlib.import_module(mod)
        except ImportError:
            for cmd_extra in [[], ["--user"], ["--break-system-packages"]]:
                try:
                    subprocess.check_call(
                        [sys.executable, "-m", "pip", "install", pkg] + cmd_extra,
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                    )
                    break
                except subprocess.CalledProcessError:
                    continue

_bootstrap()

import requests
from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QSplitter, QTextEdit, QLineEdit, QPushButton, QLabel, QComboBox,
    QFileDialog, QGroupBox, QSlider, QSpinBox, QCheckBox, QTabWidget,
    QListWidget, QListWidgetItem, QStatusBar, QMessageBox, QPlainTextEdit,
    QSizePolicy, QToolButton, QMenu, QSystemTrayIcon, QStyle
)
from PyQt6.QtCore import (
    Qt, QThread, pyqtSignal, QTimer, QSettings, QSize, QProcess
)
from PyQt6.QtGui import (
    QFont, QColor, QPalette, QIcon, QAction, QTextCursor, QFontDatabase
)

# ── Catppuccin Mocha palette ──────────────────────────────────────────────
CAT = {
    "base": "#1e1e2e", "mantle": "#181825", "crust": "#11111b",
    "surface0": "#313244", "surface1": "#45475a", "surface2": "#585b70",
    "overlay0": "#6c7086", "overlay1": "#7f849c",
    "text": "#cdd6f4", "subtext0": "#a6adc8", "subtext1": "#bac2de",
    "lavender": "#b4befe", "blue": "#89b4fa", "sapphire": "#74c7ec",
    "sky": "#89dceb", "teal": "#94e2d5", "green": "#a6e3a1",
    "yellow": "#f9e2af", "peach": "#fab387", "maroon": "#eba0ac",
    "red": "#f38ba8", "mauve": "#cba6f7", "pink": "#f5c2e7",
    "flamingo": "#f2cdcd", "rosewater": "#f5e0dc",
}

STYLESHEET = f"""
QMainWindow, QWidget {{
    background-color: {CAT['base']};
    color: {CAT['text']};
    font-family: 'Segoe UI', sans-serif;
    font-size: 13px;
}}
QGroupBox {{
    border: 1px solid {CAT['surface1']};
    border-radius: 8px;
    margin-top: 12px;
    padding-top: 16px;
    font-weight: bold;
    color: {CAT['lavender']};
}}
QGroupBox::title {{
    subcontrol-origin: margin;
    left: 12px;
    padding: 0 6px;
}}
QPushButton {{
    background-color: {CAT['surface0']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    padding: 6px 16px;
    font-weight: 500;
}}
QPushButton:hover {{
    background-color: {CAT['surface1']};
    border-color: {CAT['lavender']};
}}
QPushButton:pressed {{
    background-color: {CAT['surface2']};
}}
QPushButton#startBtn {{
    background-color: {CAT['green']};
    color: {CAT['crust']};
    font-weight: bold;
}}
QPushButton#startBtn:hover {{
    background-color: {CAT['teal']};
}}
QPushButton#stopBtn {{
    background-color: {CAT['red']};
    color: {CAT['crust']};
    font-weight: bold;
}}
QPushButton#stopBtn:hover {{
    background-color: {CAT['maroon']};
}}
QPushButton#sendBtn {{
    background-color: {CAT['blue']};
    color: {CAT['crust']};
    font-weight: bold;
    padding: 8px 24px;
    font-size: 14px;
}}
QPushButton#sendBtn:hover {{
    background-color: {CAT['sapphire']};
}}
QPushButton#sendBtn:disabled {{
    background-color: {CAT['surface1']};
    color: {CAT['overlay0']};
}}
QLineEdit, QPlainTextEdit {{
    background-color: {CAT['mantle']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    padding: 6px 10px;
    selection-background-color: {CAT['surface2']};
}}
QLineEdit:focus, QPlainTextEdit:focus {{
    border-color: {CAT['lavender']};
}}
QTextEdit {{
    background-color: {CAT['mantle']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    padding: 8px;
    selection-background-color: {CAT['surface2']};
}}
QTextEdit:focus {{
    border-color: {CAT['lavender']};
}}
QComboBox {{
    background-color: {CAT['mantle']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    padding: 5px 10px;
    min-width: 120px;
}}
QComboBox:hover {{
    border-color: {CAT['lavender']};
}}
QComboBox::drop-down {{
    border: none;
    width: 24px;
}}
QComboBox::down-arrow {{
    image: none;
    border-left: 5px solid transparent;
    border-right: 5px solid transparent;
    border-top: 6px solid {CAT['text']};
    margin-right: 8px;
}}
QComboBox QAbstractItemView {{
    background-color: {CAT['mantle']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    selection-background-color: {CAT['surface1']};
    outline: none;
}}
QSlider::groove:horizontal {{
    height: 6px;
    background: {CAT['surface0']};
    border-radius: 3px;
}}
QSlider::handle:horizontal {{
    background: {CAT['lavender']};
    width: 16px;
    height: 16px;
    margin: -5px 0;
    border-radius: 8px;
}}
QSlider::sub-page:horizontal {{
    background: {CAT['blue']};
    border-radius: 3px;
}}
QSpinBox {{
    background-color: {CAT['mantle']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    padding: 4px 8px;
}}
QSpinBox:focus {{
    border-color: {CAT['lavender']};
}}
QSpinBox::up-button, QSpinBox::down-button {{
    background: {CAT['surface0']};
    border: none;
    width: 20px;
}}
QSpinBox::up-arrow {{
    border-left: 4px solid transparent;
    border-right: 4px solid transparent;
    border-bottom: 5px solid {CAT['text']};
}}
QSpinBox::down-arrow {{
    border-left: 4px solid transparent;
    border-right: 4px solid transparent;
    border-top: 5px solid {CAT['text']};
}}
QCheckBox {{
    color: {CAT['text']};
    spacing: 6px;
}}
QCheckBox::indicator {{
    width: 18px;
    height: 18px;
    border-radius: 4px;
    border: 1px solid {CAT['surface1']};
    background: {CAT['mantle']};
}}
QCheckBox::indicator:checked {{
    background: {CAT['blue']};
    border-color: {CAT['blue']};
}}
QTabWidget::pane {{
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    background: {CAT['base']};
}}
QTabBar::tab {{
    background: {CAT['mantle']};
    color: {CAT['subtext0']};
    padding: 8px 16px;
    border-top-left-radius: 6px;
    border-top-right-radius: 6px;
    margin-right: 2px;
}}
QTabBar::tab:selected {{
    background: {CAT['base']};
    color: {CAT['lavender']};
    border-bottom: 2px solid {CAT['lavender']};
}}
QTabBar::tab:hover:!selected {{
    background: {CAT['surface0']};
    color: {CAT['text']};
}}
QListWidget {{
    background-color: {CAT['mantle']};
    color: {CAT['text']};
    border: 1px solid {CAT['surface1']};
    border-radius: 6px;
    padding: 4px;
    outline: none;
}}
QListWidget::item {{
    padding: 6px 8px;
    border-radius: 4px;
}}
QListWidget::item:selected {{
    background-color: {CAT['surface1']};
    color: {CAT['lavender']};
}}
QListWidget::item:hover:!selected {{
    background-color: {CAT['surface0']};
}}
QStatusBar {{
    background-color: {CAT['mantle']};
    color: {CAT['subtext0']};
    border-top: 1px solid {CAT['surface0']};
}}
QSplitter::handle {{
    background-color: {CAT['surface0']};
    width: 2px;
}}
QSplitter::handle:hover {{
    background-color: {CAT['lavender']};
}}
QLabel#sectionLabel {{
    color: {CAT['subtext0']};
    font-size: 11px;
    font-weight: bold;
    text-transform: uppercase;
}}
QLabel#valueLabel {{
    color: {CAT['blue']};
    font-weight: bold;
    min-width: 40px;
}}
QScrollBar:vertical {{
    background: {CAT['mantle']};
    width: 10px;
    border-radius: 5px;
}}
QScrollBar::handle:vertical {{
    background: {CAT['surface1']};
    border-radius: 5px;
    min-height: 30px;
}}
QScrollBar::handle:vertical:hover {{
    background: {CAT['surface2']};
}}
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{
    height: 0;
}}
QScrollBar:horizontal {{
    background: {CAT['mantle']};
    height: 10px;
    border-radius: 5px;
}}
QScrollBar::handle:horizontal {{
    background: {CAT['surface1']};
    border-radius: 5px;
    min-width: 30px;
}}
QScrollBar::handle:horizontal:hover {{
    background: {CAT['surface2']};
}}
QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal {{
    width: 0;
}}
"""


# ── Chat message HTML formatting ─────────────────────────────────────────
def format_message(role, content):
    colors = {
        "user": (CAT["blue"], CAT["surface0"], "You"),
        "assistant": (CAT["green"], CAT["mantle"], "Assistant"),
        "system": (CAT["mauve"], CAT["crust"], "System"),
    }
    accent, bg, label = colors.get(role, (CAT["text"], CAT["base"], role))
    escaped = (
        content.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        .replace("\n", "<br>")
    )
    return f"""
    <div style="margin: 8px 0; padding: 12px 16px; background-color: {bg};
                border-left: 3px solid {accent}; border-radius: 6px;">
        <span style="color: {accent}; font-weight: bold; font-size: 12px;">{label}</span>
        <div style="margin-top: 6px; color: {CAT['text']}; line-height: 1.5;">{escaped}</div>
    </div>"""


# ── Streaming chat worker ────────────────────────────────────────────────
class ChatWorker(QThread):
    token_received = pyqtSignal(str)
    finished_response = pyqtSignal(str)
    error_occurred = pyqtSignal(str)

    def __init__(self, url, messages, params):
        super().__init__()
        self.url = url
        self.messages = messages
        self.params = params
        self._stop = False

    def stop(self):
        self._stop = True

    def run(self):
        payload = {
            "messages": self.messages,
            "stream": True,
            **self.params,
        }
        full_response = ""
        try:
            resp = requests.post(
                f"{self.url}/v1/chat/completions",
                json=payload,
                stream=True,
                timeout=300,
            )
            resp.raise_for_status()
            for line in resp.iter_lines(decode_unicode=True):
                if self._stop:
                    break
                if not line or not line.startswith("data: "):
                    continue
                data = line[6:]
                if data.strip() == "[DONE]":
                    break
                try:
                    chunk = json.loads(data)
                    delta = chunk["choices"][0].get("delta", {})
                    token = delta.get("content", "")
                    if token:
                        full_response += token
                        self.token_received.emit(token)
                except (json.JSONDecodeError, KeyError, IndexError):
                    continue
            self.finished_response.emit(full_response)
        except requests.exceptions.ConnectionError:
            self.error_occurred.emit("Cannot connect to llama-server. Is it running?")
        except requests.exceptions.Timeout:
            self.error_occurred.emit("Request timed out.")
        except Exception as e:
            self.error_occurred.emit(str(e))


# ── Server process manager ───────────────────────────────────────────────
class ServerManager(QThread):
    log_output = pyqtSignal(str)
    server_ready = pyqtSignal()
    server_error = pyqtSignal(str)
    server_stopped = pyqtSignal()

    def __init__(self, exe_path, model_path, args):
        super().__init__()
        self.exe_path = exe_path
        self.model_path = model_path
        self.args = args
        self.process = None
        self._stop = False

    def stop(self):
        self._stop = True
        if self.process:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()

    def run(self):
        cmd = [self.exe_path, "-m", self.model_path] + self.args
        self.log_output.emit(f"Starting: {' '.join(cmd)}\n")
        try:
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = 0  # SW_HIDE
            self.process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1,
                startupinfo=startupinfo,
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
        except FileNotFoundError:
            self.server_error.emit(f"Executable not found: {self.exe_path}")
            return
        except Exception as e:
            self.server_error.emit(str(e))
            return

        ready_emitted = False
        for line in iter(self.process.stdout.readline, ""):
            if self._stop:
                break
            self.log_output.emit(line)
            if not ready_emitted and ("listening" in line.lower() or "http" in line.lower()):
                ready_emitted = True
                self.server_ready.emit()

        self.process.stdout.close()
        self.process.wait()
        self.server_stopped.emit()


# ── Model scanner ────────────────────────────────────────────────────────
def scan_models(folder):
    """Recursively find all .gguf files in a folder."""
    models = []
    if not folder or not os.path.isdir(folder):
        return models
    for root, dirs, files in os.walk(folder):
        for f in files:
            if f.lower().endswith(".gguf"):
                full = os.path.join(root, f)
                size_gb = os.path.getsize(full) / (1024**3)
                models.append((f, full, size_gb))
    models.sort(key=lambda x: x[0].lower())
    return models


def detect_gpu_layers():
    """Try to detect if CUDA is available for default GPU layers suggestion."""
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.total", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=5,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        if result.returncode == 0:
            return 99  # Offload all layers by default if GPU detected
    except Exception:
        pass
    return 0


# ── Main window ──────────────────────────────────────────────────────────
class LlamaLinkWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(f"{APP_NAME} v{APP_VERSION}")
        self.resize(1200, 800)
        self.settings = QSettings(APP_NAME, APP_NAME)
        self.server_thread = None
        self.chat_worker = None
        self.messages = []
        self.streaming = False
        self._stream_buffer = ""

        self._build_ui()
        self._load_settings()
        self._connect_signals()

        self.statusBar().showMessage("Ready - Configure server path and model to begin")

    # ── UI Construction ──────────────────────────────────────────────────
    def _build_ui(self):
        central = QWidget()
        self.setCentralWidget(central)
        main_layout = QHBoxLayout(central)
        main_layout.setContentsMargins(8, 8, 8, 8)
        main_layout.setSpacing(8)

        splitter = QSplitter(Qt.Orientation.Horizontal)
        main_layout.addWidget(splitter)

        # ── Left panel: Config ───────────────────────────────────────────
        left_panel = QWidget()
        left_layout = QVBoxLayout(left_panel)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(6)

        # Server executable
        server_group = QGroupBox("Server")
        sg_layout = QVBoxLayout(server_group)

        exe_row = QHBoxLayout()
        self.exe_path_edit = QLineEdit()
        self.exe_path_edit.setPlaceholderText("Path to llama-server.exe...")
        exe_browse = QPushButton("Browse")
        exe_browse.clicked.connect(self._browse_exe)
        exe_row.addWidget(self.exe_path_edit, 1)
        exe_row.addWidget(exe_browse)
        sg_layout.addLayout(exe_row)

        port_row = QHBoxLayout()
        port_row.addWidget(QLabel("Port:"))
        self.port_spin = QSpinBox()
        self.port_spin.setRange(1024, 65535)
        self.port_spin.setValue(8080)
        port_row.addWidget(self.port_spin)
        port_row.addStretch()

        self.start_btn = QPushButton("Start Server")
        self.start_btn.setObjectName("startBtn")
        self.stop_btn = QPushButton("Stop")
        self.stop_btn.setObjectName("stopBtn")
        self.stop_btn.setEnabled(False)
        port_row.addWidget(self.start_btn)
        port_row.addWidget(self.stop_btn)
        sg_layout.addLayout(port_row)

        self.server_status_label = QLabel("Stopped")
        self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
        sg_layout.addWidget(self.server_status_label)

        left_layout.addWidget(server_group)

        # Model selection
        model_group = QGroupBox("Model")
        mg_layout = QVBoxLayout(model_group)

        folder_row = QHBoxLayout()
        self.model_folder_edit = QLineEdit()
        self.model_folder_edit.setPlaceholderText("Model folder path...")
        folder_browse = QPushButton("Browse")
        folder_browse.clicked.connect(self._browse_model_folder)
        folder_row.addWidget(self.model_folder_edit, 1)
        folder_row.addWidget(folder_browse)
        mg_layout.addLayout(folder_row)

        self.model_combo = QComboBox()
        self.model_combo.setPlaceholderText("Select a model...")
        mg_layout.addWidget(self.model_combo)

        self.model_info_label = QLabel("")
        self.model_info_label.setStyleSheet(f"color: {CAT['subtext0']}; font-size: 11px;")
        mg_layout.addWidget(self.model_info_label)

        left_layout.addWidget(model_group)

        # Parameters
        params_group = QGroupBox("Parameters")
        pg_layout = QVBoxLayout(params_group)

        # Context size
        ctx_row = QHBoxLayout()
        ctx_row.addWidget(QLabel("Context Size:"))
        self.ctx_spin = QSpinBox()
        self.ctx_spin.setRange(256, 131072)
        self.ctx_spin.setValue(4096)
        self.ctx_spin.setSingleStep(256)
        ctx_row.addWidget(self.ctx_spin)
        pg_layout.addLayout(ctx_row)

        # GPU Layers
        gpu_row = QHBoxLayout()
        gpu_row.addWidget(QLabel("GPU Layers:"))
        self.gpu_spin = QSpinBox()
        self.gpu_spin.setRange(0, 999)
        self.gpu_spin.setValue(detect_gpu_layers())
        gpu_row.addWidget(self.gpu_spin)
        pg_layout.addLayout(gpu_row)

        # Threads
        cpu_count = os.cpu_count() or 4
        threads_row = QHBoxLayout()
        threads_row.addWidget(QLabel("Threads:"))
        self.threads_spin = QSpinBox()
        self.threads_spin.setRange(1, cpu_count * 2)
        self.threads_spin.setValue(max(1, cpu_count // 2))
        threads_row.addWidget(self.threads_spin)
        pg_layout.addLayout(threads_row)

        # Temperature
        temp_row = QHBoxLayout()
        temp_row.addWidget(QLabel("Temperature:"))
        self.temp_slider = QSlider(Qt.Orientation.Horizontal)
        self.temp_slider.setRange(0, 200)
        self.temp_slider.setValue(70)
        self.temp_label = QLabel("0.70")
        self.temp_label.setObjectName("valueLabel")
        self.temp_slider.valueChanged.connect(
            lambda v: self.temp_label.setText(f"{v/100:.2f}")
        )
        temp_row.addWidget(self.temp_slider, 1)
        temp_row.addWidget(self.temp_label)
        pg_layout.addLayout(temp_row)

        # Top P
        topp_row = QHBoxLayout()
        topp_row.addWidget(QLabel("Top P:"))
        self.topp_slider = QSlider(Qt.Orientation.Horizontal)
        self.topp_slider.setRange(0, 100)
        self.topp_slider.setValue(90)
        self.topp_label = QLabel("0.90")
        self.topp_label.setObjectName("valueLabel")
        self.topp_slider.valueChanged.connect(
            lambda v: self.topp_label.setText(f"{v/100:.2f}")
        )
        topp_row.addWidget(self.topp_slider, 1)
        topp_row.addWidget(self.topp_label)
        pg_layout.addLayout(topp_row)

        # Top K
        topk_row = QHBoxLayout()
        topk_row.addWidget(QLabel("Top K:"))
        self.topk_spin = QSpinBox()
        self.topk_spin.setRange(0, 500)
        self.topk_spin.setValue(40)
        topk_row.addWidget(self.topk_spin)
        pg_layout.addLayout(topk_row)

        # Repeat penalty
        rep_row = QHBoxLayout()
        rep_row.addWidget(QLabel("Repeat Penalty:"))
        self.repeat_slider = QSlider(Qt.Orientation.Horizontal)
        self.repeat_slider.setRange(100, 200)
        self.repeat_slider.setValue(110)
        self.repeat_label = QLabel("1.10")
        self.repeat_label.setObjectName("valueLabel")
        self.repeat_slider.valueChanged.connect(
            lambda v: self.repeat_label.setText(f"{v/100:.2f}")
        )
        rep_row.addWidget(self.repeat_slider, 1)
        rep_row.addWidget(self.repeat_label)
        pg_layout.addLayout(rep_row)

        # Max tokens
        max_row = QHBoxLayout()
        max_row.addWidget(QLabel("Max Tokens:"))
        self.max_tokens_spin = QSpinBox()
        self.max_tokens_spin.setRange(-1, 32768)
        self.max_tokens_spin.setValue(2048)
        self.max_tokens_spin.setSpecialValueText("Unlimited")
        max_row.addWidget(self.max_tokens_spin)
        pg_layout.addLayout(max_row)

        left_layout.addWidget(params_group)

        # System prompt
        sys_group = QGroupBox("System Prompt")
        sys_layout = QVBoxLayout(sys_group)
        self.system_prompt = QPlainTextEdit()
        self.system_prompt.setPlaceholderText("Enter system prompt (optional)...")
        self.system_prompt.setMaximumHeight(100)
        sys_layout.addWidget(self.system_prompt)
        left_layout.addWidget(sys_group)

        # Presets
        preset_row = QHBoxLayout()
        self.preset_combo = QComboBox()
        self.preset_combo.addItems([
            "Default", "Creative", "Precise", "Code", "Roleplay"
        ])
        self.preset_combo.currentTextChanged.connect(self._apply_preset)
        preset_row.addWidget(QLabel("Preset:"))
        preset_row.addWidget(self.preset_combo, 1)
        left_layout.addLayout(preset_row)

        left_layout.addStretch()

        # ── Right panel: Chat + Logs ─────────────────────────────────────
        right_panel = QWidget()
        right_layout = QVBoxLayout(right_panel)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(6)

        tabs = QTabWidget()

        # Chat tab
        chat_widget = QWidget()
        chat_layout = QVBoxLayout(chat_widget)
        chat_layout.setContentsMargins(0, 0, 0, 0)
        chat_layout.setSpacing(6)

        # Chat display
        self.chat_display = QTextEdit()
        self.chat_display.setReadOnly(True)
        self.chat_display.setFont(QFont("Segoe UI", 12))
        chat_layout.addWidget(self.chat_display, 1)

        # Input area
        input_row = QHBoxLayout()
        self.input_edit = QTextEdit()
        self.input_edit.setPlaceholderText("Type your message... (Enter to send, Shift+Enter for new line)")
        self.input_edit.setMaximumHeight(100)
        self.input_edit.setFont(QFont("Segoe UI", 12))
        self.send_btn = QPushButton("Send")
        self.send_btn.setObjectName("sendBtn")
        self.send_btn.setMinimumHeight(50)
        self.stop_gen_btn = QPushButton("Stop")
        self.stop_gen_btn.setObjectName("stopBtn")
        self.stop_gen_btn.setMinimumHeight(50)
        self.stop_gen_btn.setVisible(False)
        input_row.addWidget(self.input_edit, 1)
        btn_col = QVBoxLayout()
        btn_col.addWidget(self.send_btn)
        btn_col.addWidget(self.stop_gen_btn)
        input_row.addLayout(btn_col)
        chat_layout.addLayout(input_row)

        # Chat controls
        ctrl_row = QHBoxLayout()
        self.new_chat_btn = QPushButton("New Chat")
        self.new_chat_btn.clicked.connect(self._new_chat)
        ctrl_row.addWidget(self.new_chat_btn)
        ctrl_row.addStretch()
        self.token_count_label = QLabel("")
        self.token_count_label.setStyleSheet(f"color: {CAT['subtext0']}; font-size: 11px;")
        ctrl_row.addWidget(self.token_count_label)
        chat_layout.addLayout(ctrl_row)

        tabs.addTab(chat_widget, "Chat")

        # Server log tab
        self.server_log = QPlainTextEdit()
        self.server_log.setReadOnly(True)
        self.server_log.setFont(QFont("Consolas", 10))
        self.server_log.setStyleSheet(
            f"background-color: {CAT['crust']}; color: {CAT['green']}; "
            f"border: none; padding: 8px;"
        )
        tabs.addTab(self.server_log, "Server Log")

        right_layout.addWidget(tabs)

        # Add to splitter
        splitter.addWidget(left_panel)
        splitter.addWidget(right_panel)
        splitter.setSizes([350, 850])
        splitter.setStretchFactor(0, 0)
        splitter.setStretchFactor(1, 1)

        # Status bar
        self.setStatusBar(QStatusBar())

    # ── Signals ──────────────────────────────────────────────────────────
    def _connect_signals(self):
        self.start_btn.clicked.connect(self._start_server)
        self.stop_btn.clicked.connect(self._stop_server)
        self.send_btn.clicked.connect(self._send_message)
        self.stop_gen_btn.clicked.connect(self._stop_generation)
        self.model_folder_edit.textChanged.connect(self._refresh_models)
        self.model_combo.currentIndexChanged.connect(self._on_model_selected)

        # Enter to send (Shift+Enter for newline)
        self.input_edit.installEventFilter(self)

    def eventFilter(self, obj, event):
        if obj == self.input_edit and event.type() == event.Type.KeyPress:
            if event.key() == Qt.Key.Key_Return and not (event.modifiers() & Qt.KeyboardModifier.ShiftModifier):
                self._send_message()
                return True
        return super().eventFilter(obj, event)

    # ── Browse dialogs ───────────────────────────────────────────────────
    def _browse_exe(self):
        path, _ = QFileDialog.getOpenFileName(
            self, "Select llama-server executable",
            self.exe_path_edit.text() or "",
            "Executables (*.exe);;All Files (*)"
        )
        if path:
            self.exe_path_edit.setText(path)

    def _browse_model_folder(self):
        folder = QFileDialog.getExistingDirectory(
            self, "Select Model Folder",
            self.model_folder_edit.text() or ""
        )
        if folder:
            self.model_folder_edit.setText(folder)

    # ── Model scanning ───────────────────────────────────────────────────
    def _refresh_models(self, folder):
        self.model_combo.clear()
        models = scan_models(folder)
        for name, path, size_gb in models:
            self.model_combo.addItem(f"{name}  ({size_gb:.1f} GB)", path)
        if models:
            self.statusBar().showMessage(f"Found {len(models)} model(s)")
        else:
            self.model_info_label.setText("")

    def _on_model_selected(self, index):
        if index >= 0:
            path = self.model_combo.itemData(index)
            if path:
                size_gb = os.path.getsize(path) / (1024**3)
                self.model_info_label.setText(f"{os.path.basename(path)} - {size_gb:.2f} GB")

    # ── Server management ────────────────────────────────────────────────
    def _start_server(self):
        exe = self.exe_path_edit.text().strip()
        if not exe or not os.path.isfile(exe):
            self.statusBar().showMessage("ERROR: Invalid llama-server path")
            return

        model_path = self.model_combo.currentData()
        if not model_path:
            self.statusBar().showMessage("ERROR: No model selected")
            return

        port = self.port_spin.value()
        args = [
            "--port", str(port),
            "-c", str(self.ctx_spin.value()),
            "-ngl", str(self.gpu_spin.value()),
            "-t", str(self.threads_spin.value()),
        ]

        self.server_log.clear()
        self.server_thread = ServerManager(exe, model_path, args)
        self.server_thread.log_output.connect(self._on_server_log)
        self.server_thread.server_ready.connect(self._on_server_ready)
        self.server_thread.server_error.connect(self._on_server_error)
        self.server_thread.server_stopped.connect(self._on_server_stopped)
        self.server_thread.start()

        self.start_btn.setEnabled(False)
        self.stop_btn.setEnabled(True)
        self.server_status_label.setText("Starting...")
        self.server_status_label.setStyleSheet(f"color: {CAT['yellow']}; font-weight: bold;")
        self.statusBar().showMessage("Starting server...")

    def _stop_server(self):
        if self.server_thread:
            self.server_thread.stop()
            self.statusBar().showMessage("Stopping server...")

    def _on_server_log(self, text):
        self.server_log.appendPlainText(text.rstrip())

    def _on_server_ready(self):
        self.server_status_label.setText("Running")
        self.server_status_label.setStyleSheet(f"color: {CAT['green']}; font-weight: bold;")
        self.statusBar().showMessage("Server is ready")

    def _on_server_error(self, msg):
        self.server_status_label.setText("Error")
        self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
        self.statusBar().showMessage(f"Server error: {msg}")
        self.start_btn.setEnabled(True)
        self.stop_btn.setEnabled(False)

    def _on_server_stopped(self):
        self.server_status_label.setText("Stopped")
        self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
        self.start_btn.setEnabled(True)
        self.stop_btn.setEnabled(False)
        self.statusBar().showMessage("Server stopped")

    # ── Chat ─────────────────────────────────────────────────────────────
    def _send_message(self):
        text = self.input_edit.toPlainText().strip()
        if not text or self.streaming:
            return

        self.input_edit.clear()

        # Add system prompt on first message
        if not self.messages:
            sys_prompt = self.system_prompt.toPlainText().strip()
            if sys_prompt:
                self.messages.append({"role": "system", "content": sys_prompt})
                self.chat_display.append(format_message("system", sys_prompt))

        self.messages.append({"role": "user", "content": text})
        self.chat_display.append(format_message("user", text))

        # Build API params
        params = {
            "temperature": self.temp_slider.value() / 100.0,
            "top_p": self.topp_slider.value() / 100.0,
            "top_k": self.topk_spin.value(),
            "repeat_penalty": self.repeat_slider.value() / 100.0,
        }
        max_tokens = self.max_tokens_spin.value()
        if max_tokens > 0:
            params["max_tokens"] = max_tokens

        port = self.port_spin.value()
        url = f"http://127.0.0.1:{port}"

        self.streaming = True
        self._stream_buffer = ""
        self.send_btn.setVisible(False)
        self.stop_gen_btn.setVisible(True)

        # Add placeholder for assistant response
        self.chat_display.append(
            f'<div style="margin: 8px 0; padding: 12px 16px; background-color: {CAT["mantle"]};'
            f' border-left: 3px solid {CAT["green"]}; border-radius: 6px;">'
            f'<span style="color: {CAT["green"]}; font-weight: bold; font-size: 12px;">Assistant</span>'
            f'<div id="streaming" style="margin-top: 6px; color: {CAT["text"]}; line-height: 1.5;">'
            f'<span style="color: {CAT["overlay0"]};">Thinking...</span></div></div>'
        )

        self.chat_worker = ChatWorker(url, list(self.messages), params)
        self.chat_worker.token_received.connect(self._on_token)
        self.chat_worker.finished_response.connect(self._on_response_done)
        self.chat_worker.error_occurred.connect(self._on_chat_error)
        self.chat_worker.start()

        self.statusBar().showMessage("Generating response...")

    def _on_token(self, token):
        self._stream_buffer += token
        # Rebuild the last message with accumulated text
        escaped = (
            self._stream_buffer
            .replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\n", "<br>")
        )
        # Remove the last block and re-add with updated content
        cursor = self.chat_display.textCursor()
        # Find and update - we just replace the entire document's last assistant block
        html = self.chat_display.toHtml()
        thinking_marker = '<span style="color: ' + CAT["overlay0"] + ';">Thinking...</span>'
        if thinking_marker in html:
            html = html.replace(thinking_marker, escaped)
        else:
            # Find the last closing div tags and insert before them
            last_div = html.rfind("</div></div>")
            if last_div >= 0:
                # We need a smarter approach - just rebuild last message
                pass

        # Simpler approach: clear and rebuild last message
        self._update_stream_display()

    def _update_stream_display(self):
        """Rebuild chat display with current stream buffer."""
        html_parts = []
        for msg in self.messages:
            html_parts.append(format_message(msg["role"], msg["content"]))

        # Add streaming assistant message
        escaped = (
            self._stream_buffer
            .replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            .replace("\n", "<br>")
        )
        display_text = escaped if escaped else f'<span style="color: {CAT["overlay0"]};">Thinking...</span>'
        html_parts.append(
            f'<div style="margin: 8px 0; padding: 12px 16px; background-color: {CAT["mantle"]};'
            f' border-left: 3px solid {CAT["green"]}; border-radius: 6px;">'
            f'<span style="color: {CAT["green"]}; font-weight: bold; font-size: 12px;">Assistant</span>'
            f'<div style="margin-top: 6px; color: {CAT["text"]}; line-height: 1.5;">{display_text}</div></div>'
        )

        self.chat_display.setHtml(
            f'<body style="background-color: {CAT["mantle"]}; margin: 0;">{"".join(html_parts)}</body>'
        )
        # Scroll to bottom
        sb = self.chat_display.verticalScrollBar()
        sb.setValue(sb.maximum())

    def _on_response_done(self, full_response):
        self.streaming = False
        self.send_btn.setVisible(True)
        self.stop_gen_btn.setVisible(False)

        if full_response:
            self.messages.append({"role": "assistant", "content": full_response})

        # Rebuild clean display
        self._rebuild_chat_display()

        word_count = len(full_response.split()) if full_response else 0
        self.token_count_label.setText(f"~{word_count} words | {len(self.messages)} messages")
        self.statusBar().showMessage("Response complete")

    def _on_chat_error(self, error):
        self.streaming = False
        self.send_btn.setVisible(True)
        self.stop_gen_btn.setVisible(False)

        # Remove the streaming placeholder and rebuild
        self._rebuild_chat_display()

        self.chat_display.append(
            f'<div style="margin: 8px 0; padding: 10px 16px; background-color: {CAT["crust"]};'
            f' border-left: 3px solid {CAT["red"]}; border-radius: 6px;">'
            f'<span style="color: {CAT["red"]}; font-weight: bold;">Error:</span> '
            f'<span style="color: {CAT["text"]};">{error}</span></div>'
        )
        sb = self.chat_display.verticalScrollBar()
        sb.setValue(sb.maximum())
        self.statusBar().showMessage(f"Error: {error}")

    def _stop_generation(self):
        if self.chat_worker:
            self.chat_worker.stop()

    def _rebuild_chat_display(self):
        html_parts = []
        for msg in self.messages:
            html_parts.append(format_message(msg["role"], msg["content"]))
        self.chat_display.setHtml(
            f'<body style="background-color: {CAT["mantle"]}; margin: 0;">{"".join(html_parts)}</body>'
        )
        sb = self.chat_display.verticalScrollBar()
        sb.setValue(sb.maximum())

    def _new_chat(self):
        self.messages.clear()
        self.chat_display.clear()
        self.token_count_label.setText("")
        self.statusBar().showMessage("New chat started")

    # ── Presets ───────────────────────────────────────────────────────────
    def _apply_preset(self, name):
        presets = {
            "Default":   {"temp": 70,  "topp": 90, "topk": 40,  "rep": 110},
            "Creative":  {"temp": 120, "topp": 95, "topk": 80,  "rep": 105},
            "Precise":   {"temp": 20,  "topp": 80, "topk": 20,  "rep": 115},
            "Code":      {"temp": 10,  "topp": 85, "topk": 30,  "rep": 100},
            "Roleplay":  {"temp": 90,  "topp": 92, "topk": 60,  "rep": 108},
        }
        p = presets.get(name)
        if not p:
            return
        self.temp_slider.setValue(p["temp"])
        self.topp_slider.setValue(p["topp"])
        self.topk_spin.setValue(p["topk"])
        self.repeat_slider.setValue(p["rep"])

    # ── Settings persistence ─────────────────────────────────────────────
    def _load_settings(self):
        self.exe_path_edit.setText(self.settings.value("exe_path", ""))
        self.model_folder_edit.setText(self.settings.value("model_folder", ""))
        self.port_spin.setValue(int(self.settings.value("port", 8080)))
        self.ctx_spin.setValue(int(self.settings.value("ctx_size", 4096)))
        self.gpu_spin.setValue(int(self.settings.value("gpu_layers", self.gpu_spin.value())))
        self.threads_spin.setValue(int(self.settings.value("threads", self.threads_spin.value())))
        self.temp_slider.setValue(int(self.settings.value("temperature", 70)))
        self.topp_slider.setValue(int(self.settings.value("top_p", 90)))
        self.topk_spin.setValue(int(self.settings.value("top_k", 40)))
        self.repeat_slider.setValue(int(self.settings.value("repeat_penalty", 110)))
        self.max_tokens_spin.setValue(int(self.settings.value("max_tokens", 2048)))
        self.system_prompt.setPlainText(self.settings.value("system_prompt", ""))

        saved_model = self.settings.value("selected_model", "")
        if saved_model:
            idx = self.model_combo.findData(saved_model)
            if idx >= 0:
                self.model_combo.setCurrentIndex(idx)

    def _save_settings(self):
        self.settings.setValue("exe_path", self.exe_path_edit.text())
        self.settings.setValue("model_folder", self.model_folder_edit.text())
        self.settings.setValue("port", self.port_spin.value())
        self.settings.setValue("ctx_size", self.ctx_spin.value())
        self.settings.setValue("gpu_layers", self.gpu_spin.value())
        self.settings.setValue("threads", self.threads_spin.value())
        self.settings.setValue("temperature", self.temp_slider.value())
        self.settings.setValue("top_p", self.topp_slider.value())
        self.settings.setValue("top_k", self.topk_spin.value())
        self.settings.setValue("repeat_penalty", self.repeat_slider.value())
        self.settings.setValue("max_tokens", self.max_tokens_spin.value())
        self.settings.setValue("system_prompt", self.system_prompt.toPlainText())
        self.settings.setValue("selected_model", self.model_combo.currentData() or "")

    def closeEvent(self, event):
        self._save_settings()
        if self.server_thread:
            self.server_thread.stop()
            self.server_thread.wait(3000)
        if self.chat_worker:
            self.chat_worker.stop()
            self.chat_worker.wait(2000)
        event.accept()


# ── Entry point ──────────────────────────────────────────────────────────
def main():
    app = QApplication(sys.argv)
    app.setStyle("Fusion")
    app.setStyleSheet(STYLESHEET)
    app.setApplicationName(APP_NAME)
    app.setApplicationVersion(APP_VERSION)

    window = LlamaLinkWindow()
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
