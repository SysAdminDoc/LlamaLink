"""
LlamaLink v0.2.0 - GUI Frontend for llama.cpp
A simple, versatile chat interface for local LLMs via llama-server.
"""

import sys, os, subprocess, json, re, time

APP_NAME = "LlamaLink"
APP_VERSION = "0.2.0"

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
    QListWidget, QListWidgetItem, QStatusBar, QPlainTextEdit,
    QSizePolicy, QScrollArea, QFrame
)
from PyQt6.QtCore import (
    Qt, QThread, pyqtSignal, QTimer, QSettings, QSize
)
from PyQt6.QtGui import QFont, QTextCursor

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
QPushButton#connectBtn {{
    background-color: {CAT['mauve']};
    color: {CAT['crust']};
    font-weight: bold;
}}
QPushButton#connectBtn:hover {{
    background-color: {CAT['pink']};
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


# ── Markdown-ish to HTML ─────────────────────────────────────────────────
def md_to_html(text):
    """Convert markdown-like text to styled HTML. Handles code blocks,
    inline code, bold, italic, and line breaks."""
    escaped = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    parts = []
    code_block_re = re.compile(r"```(\w*)\n(.*?)```", re.DOTALL)
    last = 0
    for m in code_block_re.finditer(escaped):
        parts.append(_inline_md(escaped[last:m.start()]))
        lang = m.group(1)
        code = m.group(2).rstrip("\n")
        lang_label = f'<span style="color:{CAT["overlay0"]};font-size:10px;">{lang}</span><br>' if lang else ""
        parts.append(
            f'<div style="background:{CAT["crust"]};border:1px solid {CAT["surface1"]};'
            f'border-radius:6px;padding:10px 12px;margin:6px 0;font-family:Consolas,monospace;'
            f'font-size:12px;white-space:pre-wrap;overflow-x:auto;">'
            f'{lang_label}{code}</div>'
        )
        last = m.end()
    parts.append(_inline_md(escaped[last:]))
    return "".join(parts)


def _inline_md(text):
    """Handle inline markdown: bold, italic, inline code, line breaks."""
    # Inline code
    text = re.sub(r"`([^`]+)`",
        lambda m: f'<code style="background:{CAT["surface0"]};padding:1px 5px;'
                  f'border-radius:3px;font-family:Consolas,monospace;font-size:12px;">'
                  f'{m.group(1)}</code>', text)
    # Bold
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)
    # Italic
    text = re.sub(r"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", r"<i>\1</i>", text)
    # Line breaks
    text = text.replace("\n", "<br>")
    return text


# ── Chat message HTML formatting ─────────────────────────────────────────
def format_message(role, content, render_md=True):
    colors = {
        "user": (CAT["blue"], CAT["surface0"], "You"),
        "assistant": (CAT["green"], CAT["mantle"], "Assistant"),
        "system": (CAT["mauve"], CAT["crust"], "System"),
    }
    accent, bg, label = colors.get(role, (CAT["text"], CAT["base"], role))
    body = md_to_html(content) if render_md else content.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\n", "<br>")
    return (
        f'<div style="margin:8px 0;padding:12px 16px;background-color:{bg};'
        f'border-left:3px solid {accent};border-radius:6px;">'
        f'<span style="color:{accent};font-weight:bold;font-size:12px;">{label}</span>'
        f'<div style="margin-top:6px;color:{CAT["text"]};line-height:1.5;">{body}</div></div>'
    )


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
                json=payload, stream=True, timeout=300,
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
        except requests.exceptions.HTTPError as e:
            self.error_occurred.emit(f"HTTP {e.response.status_code}: {e.response.text[:200]}")
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
        self.log_output.emit(f"$ {' '.join(cmd)}\n")
        try:
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = 0
            self.process = subprocess.Popen(
                cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, bufsize=1,
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
            if not ready_emitted and ("listening" in line.lower() or "server is listening" in line.lower()):
                ready_emitted = True
                self.server_ready.emit()

        self.process.stdout.close()
        self.process.wait()
        self.server_stopped.emit()


# ── Utilities ─────────────────────────────────────────────────────────────
def scan_models(folder):
    """Recursively find all .gguf files in a folder."""
    models = []
    if not folder or not os.path.isdir(folder):
        return models
    for root, _, files in os.walk(folder):
        for f in files:
            if f.lower().endswith(".gguf"):
                full = os.path.join(root, f)
                try:
                    size_gb = os.path.getsize(full) / (1024**3)
                    models.append((f, full, size_gb))
                except OSError:
                    pass
    models.sort(key=lambda x: x[0].lower())
    return models


def detect_gpu_layers():
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.total", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=5,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        if result.returncode == 0:
            return 99
    except Exception:
        pass
    return 0


def find_llama_server():
    """Search common locations for llama-server.exe."""
    import shutil
    # Check PATH first
    found = shutil.which("llama-server") or shutil.which("llama-server.exe")
    if found:
        return found
    # Check common install locations
    candidates = [
        os.path.expanduser("~/llama.cpp/build/bin/Release/llama-server.exe"),
        os.path.expanduser("~/llama.cpp/build/bin/llama-server.exe"),
        os.path.expanduser("~/llama.cpp/llama-server.exe"),
        "C:/llama.cpp/llama-server.exe",
        os.path.expanduser("~/Desktop/llama-server.exe"),
        os.path.expanduser("~/Downloads/llama-server.exe"),
    ]
    # Also check Program Files
    for pf in [os.environ.get("ProgramFiles", ""), os.environ.get("ProgramFiles(x86)", "")]:
        if pf:
            candidates.append(os.path.join(pf, "llama.cpp", "llama-server.exe"))
    for c in candidates:
        if os.path.isfile(c):
            return c
    return ""


def get_chat_history_dir():
    """Get the chat history storage directory."""
    d = os.path.join(os.path.expanduser("~"), ".llamalink", "chats")
    os.makedirs(d, exist_ok=True)
    return d


# ── Main window ──────────────────────────────────────────────────────────
class LlamaLinkWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(f"{APP_NAME} v{APP_VERSION}")
        self.resize(1280, 860)
        self.settings = QSettings(APP_NAME, APP_NAME)
        self.server_thread = None
        self.chat_worker = None
        self.messages = []
        self.streaming = False
        self._stream_buffer = ""
        self._stream_dirty = False
        self._token_count = 0
        self._stream_start_time = 0.0
        self._current_chat_file = None
        self._server_managed = False  # True = we started it, False = external

        self._build_ui()
        self._load_settings()
        self._connect_signals()
        self._restore_geometry()
        self._refresh_chat_history()

        # Batch streaming updates at 30fps instead of per-token
        self._stream_timer = QTimer()
        self._stream_timer.setInterval(33)
        self._stream_timer.timeout.connect(self._flush_stream)

        # Health check timer for external server connections
        self._health_timer = QTimer()
        self._health_timer.setInterval(5000)
        self._health_timer.timeout.connect(self._check_server_health)

        self.statusBar().showMessage("Ready - Configure server and model to begin")

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
        left_panel.setMinimumWidth(320)
        left_panel.setMaximumWidth(420)
        left_scroll = QScrollArea()
        left_scroll.setWidget(left_panel)
        left_scroll.setWidgetResizable(True)
        left_scroll.setFrameShape(QFrame.Shape.NoFrame)
        left_layout = QVBoxLayout(left_panel)
        left_layout.setContentsMargins(0, 0, 4, 0)
        left_layout.setSpacing(6)

        # ── Server group ─────────────────────────────────────────────────
        server_group = QGroupBox("Server")
        sg_layout = QVBoxLayout(server_group)

        # Mode toggle
        mode_row = QHBoxLayout()
        self.managed_radio = QCheckBox("Launch server")
        self.managed_radio.setChecked(True)
        self.managed_radio.toggled.connect(self._on_mode_toggle)
        mode_row.addWidget(self.managed_radio)
        mode_row.addStretch()
        sg_layout.addLayout(mode_row)

        # Server exe path (managed mode)
        self.exe_row_widget = QWidget()
        exe_row = QHBoxLayout(self.exe_row_widget)
        exe_row.setContentsMargins(0, 0, 0, 0)
        self.exe_path_edit = QLineEdit()
        self.exe_path_edit.setPlaceholderText("Path to llama-server.exe...")
        exe_browse = QPushButton("Browse")
        exe_browse.clicked.connect(self._browse_exe)
        exe_row.addWidget(self.exe_path_edit, 1)
        exe_row.addWidget(exe_browse)
        sg_layout.addWidget(self.exe_row_widget)

        # External URL (connect mode)
        self.ext_url_widget = QWidget()
        self.ext_url_widget.setVisible(False)
        ext_row = QHBoxLayout(self.ext_url_widget)
        ext_row.setContentsMargins(0, 0, 0, 0)
        ext_row.addWidget(QLabel("URL:"))
        self.ext_url_edit = QLineEdit()
        self.ext_url_edit.setPlaceholderText("http://127.0.0.1:8080")
        self.ext_url_edit.setText("http://127.0.0.1:8080")
        ext_row.addWidget(self.ext_url_edit, 1)
        sg_layout.addWidget(self.ext_url_widget)

        # Port + buttons
        port_row = QHBoxLayout()
        self.port_label = QLabel("Port:")
        port_row.addWidget(self.port_label)
        self.port_spin = QSpinBox()
        self.port_spin.setRange(1024, 65535)
        self.port_spin.setValue(8080)
        port_row.addWidget(self.port_spin)
        port_row.addStretch()

        self.start_btn = QPushButton("Start Server")
        self.start_btn.setObjectName("startBtn")
        self.connect_btn = QPushButton("Connect")
        self.connect_btn.setObjectName("connectBtn")
        self.connect_btn.setVisible(False)
        self.stop_btn = QPushButton("Stop")
        self.stop_btn.setObjectName("stopBtn")
        self.stop_btn.setEnabled(False)
        port_row.addWidget(self.start_btn)
        port_row.addWidget(self.connect_btn)
        port_row.addWidget(self.stop_btn)
        sg_layout.addLayout(port_row)

        self.server_status_label = QLabel("Stopped")
        self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
        sg_layout.addWidget(self.server_status_label)

        left_layout.addWidget(server_group)

        # ── Model group ──────────────────────────────────────────────────
        self.model_group = QGroupBox("Model")
        mg_layout = QVBoxLayout(self.model_group)

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

        left_layout.addWidget(self.model_group)

        # ── Server parameters ────────────────────────────────────────────
        self.server_params_group = QGroupBox("Server Parameters")
        spg_layout = QVBoxLayout(self.server_params_group)

        for label_text, attr, range_, default, step in [
            ("Context Size:", "ctx_spin", (256, 131072), 4096, 256),
            ("GPU Layers:", "gpu_spin", (0, 999), detect_gpu_layers(), 1),
            ("Threads:", "threads_spin", (1, (os.cpu_count() or 4) * 2), max(1, (os.cpu_count() or 4) // 2), 1),
        ]:
            row = QHBoxLayout()
            row.addWidget(QLabel(label_text))
            spin = QSpinBox()
            spin.setRange(*range_)
            spin.setValue(default)
            spin.setSingleStep(step)
            setattr(self, attr, spin)
            row.addWidget(spin)
            spg_layout.addLayout(row)

        self.flash_attn_cb = QCheckBox("Flash Attention")
        self.flash_attn_cb.setChecked(True)
        spg_layout.addWidget(self.flash_attn_cb)

        self.mlock_cb = QCheckBox("Memory Lock (mlock)")
        spg_layout.addWidget(self.mlock_cb)

        left_layout.addWidget(self.server_params_group)

        # ── Generation parameters ────────────────────────────────────────
        gen_group = QGroupBox("Generation")
        gg_layout = QVBoxLayout(gen_group)

        # Temperature
        for label_text, slider_attr, label_attr, range_, default in [
            ("Temperature:", "temp_slider", "temp_label", (0, 200), 70),
            ("Top P:", "topp_slider", "topp_label", (0, 100), 90),
            ("Repeat Penalty:", "repeat_slider", "repeat_label", (100, 200), 110),
        ]:
            row = QHBoxLayout()
            row.addWidget(QLabel(label_text))
            slider = QSlider(Qt.Orientation.Horizontal)
            slider.setRange(*range_)
            slider.setValue(default)
            label = QLabel(f"{default/100:.2f}")
            label.setObjectName("valueLabel")
            slider.valueChanged.connect(lambda v, l=label: l.setText(f"{v/100:.2f}"))
            setattr(self, slider_attr, slider)
            setattr(self, label_attr, label)
            row.addWidget(slider, 1)
            row.addWidget(label)
            gg_layout.addLayout(row)

        # Top K
        topk_row = QHBoxLayout()
        topk_row.addWidget(QLabel("Top K:"))
        self.topk_spin = QSpinBox()
        self.topk_spin.setRange(0, 500)
        self.topk_spin.setValue(40)
        topk_row.addWidget(self.topk_spin)
        gg_layout.addLayout(topk_row)

        # Max tokens
        max_row = QHBoxLayout()
        max_row.addWidget(QLabel("Max Tokens:"))
        self.max_tokens_spin = QSpinBox()
        self.max_tokens_spin.setRange(-1, 131072)
        self.max_tokens_spin.setValue(2048)
        self.max_tokens_spin.setSpecialValueText("Unlimited")
        max_row.addWidget(self.max_tokens_spin)
        gg_layout.addLayout(max_row)

        # Preset
        preset_row = QHBoxLayout()
        preset_row.addWidget(QLabel("Preset:"))
        self.preset_combo = QComboBox()
        self.preset_combo.addItems(["Default", "Creative", "Precise", "Code", "Roleplay"])
        self.preset_combo.currentTextChanged.connect(self._apply_preset)
        preset_row.addWidget(self.preset_combo, 1)
        gg_layout.addLayout(preset_row)

        left_layout.addWidget(gen_group)

        # ── System prompt ────────────────────────────────────────────────
        sys_group = QGroupBox("System Prompt")
        sys_layout = QVBoxLayout(sys_group)
        self.system_prompt = QPlainTextEdit()
        self.system_prompt.setPlaceholderText("Enter system prompt (optional)...")
        self.system_prompt.setMaximumHeight(80)
        sys_layout.addWidget(self.system_prompt)
        left_layout.addWidget(sys_group)

        # ── Chat history ─────────────────────────────────────────────────
        hist_group = QGroupBox("Chat History")
        hg_layout = QVBoxLayout(hist_group)
        self.history_list = QListWidget()
        self.history_list.setMaximumHeight(140)
        self.history_list.itemClicked.connect(self._load_chat_from_history)
        hg_layout.addWidget(self.history_list)

        hist_btns = QHBoxLayout()
        self.export_btn = QPushButton("Export")
        self.export_btn.clicked.connect(self._export_chat)
        self.delete_hist_btn = QPushButton("Delete")
        self.delete_hist_btn.clicked.connect(self._delete_selected_chat)
        hist_btns.addWidget(self.export_btn)
        hist_btns.addWidget(self.delete_hist_btn)
        hist_btns.addStretch()
        hg_layout.addLayout(hist_btns)
        left_layout.addWidget(hist_group)

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

        self.chat_display = QTextEdit()
        self.chat_display.setReadOnly(True)
        self.chat_display.setFont(QFont("Segoe UI", 12))
        chat_layout.addWidget(self.chat_display, 1)

        # Input
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
        self.speed_label = QLabel("")
        self.speed_label.setStyleSheet(f"color: {CAT['peach']}; font-size: 11px; font-weight: bold;")
        ctrl_row.addWidget(self.speed_label)
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
        self.server_log.setMaximumBlockCount(5000)
        tabs.addTab(self.server_log, "Server Log")

        right_layout.addWidget(tabs)

        # Add to splitter
        splitter.addWidget(left_scroll)
        splitter.addWidget(right_panel)
        splitter.setSizes([370, 910])
        splitter.setStretchFactor(0, 0)
        splitter.setStretchFactor(1, 1)

        self.setStatusBar(QStatusBar())

    # ── Signals ──────────────────────────────────────────────────────────
    def _connect_signals(self):
        self.start_btn.clicked.connect(self._start_server)
        self.connect_btn.clicked.connect(self._connect_external)
        self.stop_btn.clicked.connect(self._stop_server)
        self.send_btn.clicked.connect(self._send_message)
        self.stop_gen_btn.clicked.connect(self._stop_generation)
        self.model_folder_edit.textChanged.connect(self._refresh_models)
        self.model_combo.currentIndexChanged.connect(self._on_model_selected)
        self.input_edit.installEventFilter(self)

    def eventFilter(self, obj, event):
        if obj == self.input_edit and event.type() == event.Type.KeyPress:
            if event.key() == Qt.Key.Key_Return and not (event.modifiers() & Qt.KeyboardModifier.ShiftModifier):
                self._send_message()
                return True
        return super().eventFilter(obj, event)

    # ── Mode toggle ──────────────────────────────────────────────────────
    def _on_mode_toggle(self, managed):
        self.exe_row_widget.setVisible(managed)
        self.ext_url_widget.setVisible(not managed)
        self.port_label.setVisible(managed)
        self.port_spin.setVisible(managed)
        self.start_btn.setVisible(managed)
        self.connect_btn.setVisible(not managed)
        self.model_group.setVisible(managed)
        self.server_params_group.setVisible(managed)

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
            if path and os.path.isfile(path):
                size_gb = os.path.getsize(path) / (1024**3)
                self.model_info_label.setText(f"{os.path.basename(path)} - {size_gb:.2f} GB")

    # ── Server management ────────────────────────────────────────────────
    def _get_server_url(self):
        if self._server_managed:
            return f"http://127.0.0.1:{self.port_spin.value()}"
        return self.ext_url_edit.text().strip().rstrip("/")

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
        if self.flash_attn_cb.isChecked():
            args.append("-fa")
        if self.mlock_cb.isChecked():
            args.append("--mlock")

        self.server_log.clear()
        self._server_managed = True
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

    def _connect_external(self):
        url = self.ext_url_edit.text().strip().rstrip("/")
        if not url:
            self.statusBar().showMessage("ERROR: Enter a server URL")
            return

        self.server_status_label.setText("Connecting...")
        self.server_status_label.setStyleSheet(f"color: {CAT['yellow']}; font-weight: bold;")
        self.statusBar().showMessage(f"Connecting to {url}...")
        QApplication.processEvents()

        try:
            resp = requests.get(f"{url}/health", timeout=5)
            if resp.status_code == 200:
                self._server_managed = False
                self.connect_btn.setEnabled(False)
                self.stop_btn.setEnabled(True)
                self._on_server_ready()
                self._health_timer.start()
                self.server_log.appendPlainText(f"Connected to external server: {url}")
            else:
                self.server_status_label.setText("Error")
                self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
                self.statusBar().showMessage(f"Server returned HTTP {resp.status_code}")
        except requests.exceptions.ConnectionError:
            self.server_status_label.setText("Error")
            self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
            self.statusBar().showMessage(f"Cannot connect to {url}")
        except Exception as e:
            self.server_status_label.setText("Error")
            self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
            self.statusBar().showMessage(str(e))

    def _check_server_health(self):
        if self._server_managed:
            return
        url = self._get_server_url()
        try:
            resp = requests.get(f"{url}/health", timeout=3)
            if resp.status_code != 200:
                self._on_server_stopped()
                self._health_timer.stop()
        except Exception:
            self._on_server_stopped()
            self._health_timer.stop()

    def _stop_server(self):
        if self._server_managed and self.server_thread:
            self.server_thread.stop()
            self.statusBar().showMessage("Stopping server...")
        else:
            # Disconnect from external
            self._health_timer.stop()
            self._on_server_stopped()
            self.statusBar().showMessage("Disconnected from external server")

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
        self.connect_btn.setEnabled(True)
        self.stop_btn.setEnabled(False)

    def _on_server_stopped(self):
        self.server_status_label.setText("Stopped")
        self.server_status_label.setStyleSheet(f"color: {CAT['red']}; font-weight: bold;")
        self.start_btn.setEnabled(True)
        self.connect_btn.setEnabled(True)
        self.stop_btn.setEnabled(False)
        self.statusBar().showMessage("Server stopped")

    # ── Chat ─────────────────────────────────────────────────────────────
    def _send_message(self):
        text = self.input_edit.toPlainText().strip()
        if not text or self.streaming:
            return

        self.input_edit.clear()

        if not self.messages:
            sys_prompt = self.system_prompt.toPlainText().strip()
            if sys_prompt:
                self.messages.append({"role": "system", "content": sys_prompt})
                self.chat_display.append(format_message("system", sys_prompt))

        self.messages.append({"role": "user", "content": text})
        self.chat_display.append(format_message("user", text, render_md=False))

        params = {
            "temperature": self.temp_slider.value() / 100.0,
            "top_p": self.topp_slider.value() / 100.0,
            "top_k": self.topk_spin.value(),
            "repeat_penalty": self.repeat_slider.value() / 100.0,
        }
        max_tokens = self.max_tokens_spin.value()
        if max_tokens > 0:
            params["max_tokens"] = max_tokens

        url = self._get_server_url()

        self.streaming = True
        self._stream_buffer = ""
        self._stream_dirty = False
        self._token_count = 0
        self._stream_start_time = time.monotonic()
        self.send_btn.setVisible(False)
        self.stop_gen_btn.setVisible(True)
        self.speed_label.setText("")

        # Show thinking indicator
        self.chat_display.append(
            f'<div style="margin:8px 0;padding:12px 16px;background-color:{CAT["mantle"]};'
            f'border-left:3px solid {CAT["green"]};border-radius:6px;">'
            f'<span style="color:{CAT["green"]};font-weight:bold;font-size:12px;">Assistant</span>'
            f'<div style="margin-top:6px;color:{CAT["overlay0"]};line-height:1.5;">Thinking...</div></div>'
        )

        self.chat_worker = ChatWorker(url, list(self.messages), params)
        self.chat_worker.token_received.connect(self._on_token)
        self.chat_worker.finished_response.connect(self._on_response_done)
        self.chat_worker.error_occurred.connect(self._on_chat_error)
        self.chat_worker.start()
        self._stream_timer.start()

        self.statusBar().showMessage("Generating response...")

    def _on_token(self, token):
        self._stream_buffer += token
        self._token_count += 1
        self._stream_dirty = True

    def _flush_stream(self):
        """Called by timer at 30fps to batch-update the display."""
        if not self._stream_dirty:
            return
        self._stream_dirty = False

        # Update speed display
        elapsed = time.monotonic() - self._stream_start_time
        if elapsed > 0.5:
            tps = self._token_count / elapsed
            self.speed_label.setText(f"{tps:.1f} tok/s")

        # Rebuild display
        html_parts = []
        for msg in self.messages:
            render = msg["role"] != "user"
            html_parts.append(format_message(msg["role"], msg["content"], render_md=render))

        escaped = md_to_html(self._stream_buffer) if self._stream_buffer else f'<span style="color:{CAT["overlay0"]};">Thinking...</span>'
        html_parts.append(
            f'<div style="margin:8px 0;padding:12px 16px;background-color:{CAT["mantle"]};'
            f'border-left:3px solid {CAT["green"]};border-radius:6px;">'
            f'<span style="color:{CAT["green"]};font-weight:bold;font-size:12px;">Assistant</span>'
            f'<div style="margin-top:6px;color:{CAT["text"]};line-height:1.5;">{escaped}</div></div>'
        )

        self.chat_display.setHtml(
            f'<body style="background-color:{CAT["mantle"]};margin:0;">{"".join(html_parts)}</body>'
        )
        sb = self.chat_display.verticalScrollBar()
        sb.setValue(sb.maximum())

    def _on_response_done(self, full_response):
        self._stream_timer.stop()
        self.streaming = False
        self.send_btn.setVisible(True)
        self.stop_gen_btn.setVisible(False)

        # Final speed
        elapsed = time.monotonic() - self._stream_start_time
        if elapsed > 0 and self._token_count > 0:
            tps = self._token_count / elapsed
            self.speed_label.setText(f"{tps:.1f} tok/s ({self._token_count} tokens in {elapsed:.1f}s)")

        if full_response:
            self.messages.append({"role": "assistant", "content": full_response})

        self._rebuild_chat_display()

        msg_count = len([m for m in self.messages if m["role"] != "system"])
        self.token_count_label.setText(f"{msg_count} messages")
        self.statusBar().showMessage("Response complete")

        # Auto-save chat
        self._save_current_chat()

    def _on_chat_error(self, error):
        self._stream_timer.stop()
        self.streaming = False
        self.send_btn.setVisible(True)
        self.stop_gen_btn.setVisible(False)
        self.speed_label.setText("")

        self._rebuild_chat_display()

        self.chat_display.append(
            f'<div style="margin:8px 0;padding:10px 16px;background-color:{CAT["crust"]};'
            f'border-left:3px solid {CAT["red"]};border-radius:6px;">'
            f'<span style="color:{CAT["red"]};font-weight:bold;">Error:</span> '
            f'<span style="color:{CAT["text"]};">{error}</span></div>'
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
            render = msg["role"] != "user"
            html_parts.append(format_message(msg["role"], msg["content"], render_md=render))
        self.chat_display.setHtml(
            f'<body style="background-color:{CAT["mantle"]};margin:0;">{"".join(html_parts)}</body>'
        )
        sb = self.chat_display.verticalScrollBar()
        sb.setValue(sb.maximum())

    def _new_chat(self):
        if self.messages:
            self._save_current_chat()
        self.messages.clear()
        self.chat_display.clear()
        self.token_count_label.setText("")
        self.speed_label.setText("")
        self._current_chat_file = None
        self.statusBar().showMessage("New chat started")

    # ── Chat history persistence ─────────────────────────────────────────
    def _save_current_chat(self):
        if not self.messages:
            return
        hist_dir = get_chat_history_dir()

        # Generate filename from first user message
        if not self._current_chat_file:
            first_user = next((m["content"] for m in self.messages if m["role"] == "user"), "chat")
            slug = re.sub(r"[^\w\s-]", "", first_user[:50]).strip().replace(" ", "_")
            ts = time.strftime("%Y%m%d_%H%M%S")
            self._current_chat_file = os.path.join(hist_dir, f"{ts}_{slug}.json")

        data = {"messages": self.messages, "timestamp": time.time()}
        with open(self._current_chat_file, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)

        self._refresh_chat_history()

    def _refresh_chat_history(self):
        self.history_list.clear()
        hist_dir = get_chat_history_dir()
        files = sorted(
            [f for f in os.listdir(hist_dir) if f.endswith(".json")],
            reverse=True,
        )
        for fname in files[:50]:
            fpath = os.path.join(hist_dir, fname)
            try:
                with open(fpath, "r", encoding="utf-8") as f:
                    data = json.load(f)
                first_user = next(
                    (m["content"][:60] for m in data.get("messages", []) if m["role"] == "user"),
                    fname,
                )
                msg_count = len([m for m in data.get("messages", []) if m["role"] != "system"])
                item = QListWidgetItem(f"{first_user}  [{msg_count} msgs]")
                item.setData(Qt.ItemDataRole.UserRole, fpath)
                self.history_list.addItem(item)
            except (json.JSONDecodeError, OSError):
                pass

    def _load_chat_from_history(self, item):
        fpath = item.data(Qt.ItemDataRole.UserRole)
        try:
            with open(fpath, "r", encoding="utf-8") as f:
                data = json.load(f)
            self.messages = data.get("messages", [])
            self._current_chat_file = fpath
            self._rebuild_chat_display()
            msg_count = len([m for m in self.messages if m["role"] != "system"])
            self.token_count_label.setText(f"{msg_count} messages")
            self.speed_label.setText("")
            self.statusBar().showMessage(f"Loaded chat: {os.path.basename(fpath)}")
        except Exception as e:
            self.statusBar().showMessage(f"Failed to load chat: {e}")

    def _export_chat(self):
        if not self.messages:
            self.statusBar().showMessage("No chat to export")
            return
        path, _ = QFileDialog.getSaveFileName(
            self, "Export Chat", "", "Markdown (*.md);;JSON (*.json);;Text (*.txt)"
        )
        if not path:
            return
        ext = os.path.splitext(path)[1].lower()
        with open(path, "w", encoding="utf-8") as f:
            if ext == ".json":
                json.dump({"messages": self.messages}, f, indent=2, ensure_ascii=False)
            elif ext == ".md":
                for msg in self.messages:
                    role = msg["role"].capitalize()
                    f.write(f"## {role}\n\n{msg['content']}\n\n---\n\n")
            else:
                for msg in self.messages:
                    role = msg["role"].capitalize()
                    f.write(f"[{role}]\n{msg['content']}\n\n")
        self.statusBar().showMessage(f"Chat exported to {path}")

    def _delete_selected_chat(self):
        item = self.history_list.currentItem()
        if not item:
            return
        fpath = item.data(Qt.ItemDataRole.UserRole)
        try:
            os.remove(fpath)
            if self._current_chat_file == fpath:
                self._current_chat_file = None
            self._refresh_chat_history()
            self.statusBar().showMessage("Chat deleted")
        except OSError as e:
            self.statusBar().showMessage(f"Failed to delete: {e}")

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
        exe = self.settings.value("exe_path", "")
        if not exe:
            exe = find_llama_server()
        self.exe_path_edit.setText(exe)
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
        self.flash_attn_cb.setChecked(self.settings.value("flash_attn", "true") == "true")
        self.mlock_cb.setChecked(self.settings.value("mlock", "false") == "true")
        self.ext_url_edit.setText(self.settings.value("ext_url", "http://127.0.0.1:8080"))

        managed = self.settings.value("managed_mode", "true") == "true"
        self.managed_radio.setChecked(managed)
        self._on_mode_toggle(managed)

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
        self.settings.setValue("flash_attn", "true" if self.flash_attn_cb.isChecked() else "false")
        self.settings.setValue("mlock", "true" if self.mlock_cb.isChecked() else "false")
        self.settings.setValue("ext_url", self.ext_url_edit.text())
        self.settings.setValue("managed_mode", "true" if self.managed_radio.isChecked() else "false")

    def _restore_geometry(self):
        geo = self.settings.value("window_geometry")
        if geo:
            self.restoreGeometry(geo)

    def closeEvent(self, event):
        self._save_settings()
        self.settings.setValue("window_geometry", self.saveGeometry())
        if self.messages:
            self._save_current_chat()
        self._health_timer.stop()
        self._stream_timer.stop()
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
