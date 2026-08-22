
# convert.py
# Универсальный конвертер YOLO -> ONNX с русским GUI.
# Python 3.11
#
# Возможности:
# - выбор .pt/.pth модели через GUI;
# - автоматическое обнаружение моделей в текущей папке и папке models;
# - выбор размера входа 320/416/512/640/960;
# - выбор OPSET;
# - simplify / dynamic / FP16;
# - CPU/GPU режим экспорта;
# - сохранение настроек в convert_settings.json;
# - отдельный процесс экспорта;
# - журнал процесса;
# - остановка экспорта;
# - повторный экспорт;
# - проверка полученного ONNX;
# - попытка определить классы исходной модели.
#
# Классы объекта не задаются конвертером.
# Они сохраняются из исходной обученной модели.

import json
import os
import queue
import subprocess
import sys
import threading
import time
import traceback
from pathlib import Path
import tkinter as tk
from tkinter import ttk, filedialog, messagebox


APP_NAME = "P-ESP — Конвертер YOLO → ONNX"
BASE_DIR = Path(__file__).resolve().parent
SETTINGS_FILE = BASE_DIR / "convert_settings.json"

DEFAULTS = {
    "model": "",
    "output_dir": str(BASE_DIR / "models"),
    "output_name": "",
    "imgsz": 320,
    "opset": 12,
    "simplify": True,
    "dynamic": False,
    "half": False,
    "device": "cpu",
    "workers": 1,
    "batch": 1,
}


def safe_load_json(path, default):
    try:
        if not path.exists():
            return dict(default)
        with path.open("r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            return dict(default)
        result = dict(default)
        result.update(data)
        return result
    except Exception:
        return dict(default)


def safe_save_json(path, data):
    try:
        with path.open("w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        return True
    except Exception:
        return False


def find_python():
    candidates = [
        Path(os.environ.get("USERPROFILE", "")) /
        "AppData/Local/Programs/Python/Python311/python.exe",
        Path(os.environ.get("LOCALAPPDATA", "")) /
        "Programs/Python/Python311/python.exe",
        Path(os.environ.get("ProgramFiles", "")) /
        "Python311/python.exe",
        Path(os.environ.get("ProgramFiles", "")) /
        "Python/Python311/python.exe",
    ]

    for candidate in candidates:
        try:
            if candidate.is_file():
                return candidate
        except Exception:
            pass

    return Path(sys.executable)


def discover_models():
    found = []
    roots = [
        BASE_DIR,
        BASE_DIR / "models",
        BASE_DIR / "weights",
    ]

    for root in roots:
        if not root.exists():
            continue

        try:
            for p in root.rglob("*"):
                if p.is_file() and p.suffix.lower() in {".pt", ".pth"}:
                    if p not in found:
                        found.append(p)
        except Exception:
            continue

    return sorted(found, key=lambda p: str(p).lower())


def pretty_size(size):
    if size < 1024:
        return f"{size} Б"
    if size < 1024 * 1024:
        return f"{size / 1024:.1f} КБ"
    if size < 1024 * 1024 * 1024:
        return f"{size / 1024 / 1024:.1f} МБ"
    return f"{size / 1024 / 1024 / 1024:.2f} ГБ"


class ExportWorker:
    def __init__(self, command, log_callback, done_callback):
        self.command = command
        self.log_callback = log_callback
        self.done_callback = done_callback
        self.process = None
        self.thread = None
        self.stop_requested = False

    def start(self):
        self.thread = threading.Thread(
            target=self._run,
            name="YOLO-ONNX-Export",
            daemon=True,
        )
        self.thread.start()

    def stop(self):
        self.stop_requested = True

        if self.process is None:
            return

        try:
            if self.process.poll() is None:
                self.process.terminate()

                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self.process.kill()
        except Exception as exc:
            self.log_callback(f"Ошибка остановки: {exc}")

    def _run(self):
        return_code = -1

        try:
            self.log_callback("Запуск отдельного процесса экспорта...")
            self.log_callback("")

            self.process = subprocess.Popen(
                self.command,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                stdin=subprocess.DEVNULL,
                text=True,
                encoding="utf-8",
                errors="replace",
                bufsize=1,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )

            while True:
                if self.stop_requested:
                    self.stop()
                    break

                line = self.process.stdout.readline()

                if line:
                    self.log_callback(line.rstrip())
                    continue

                if self.process.poll() is not None:
                    break

                time.sleep(0.05)

            return_code = self.process.returncode

            if self.stop_requested:
                self.log_callback("")
                self.log_callback("Экспорт остановлен.")
            elif return_code == 0:
                self.log_callback("")
                self.log_callback("Экспорт завершён.")
            else:
                self.log_callback("")
                self.log_callback(
                    f"Экспорт завершился с кодом {return_code}."
                )

        except Exception:
            self.log_callback(traceback.format_exc())
        finally:
            self.done_callback(return_code)


class App(tk.Tk):
    def __init__(self):
        super().__init__()

        self.title(APP_NAME)
        self.geometry("980x720")
        self.minsize(900, 650)
        self.configure(bg="#101216")

        self.settings = safe_load_json(SETTINGS_FILE, DEFAULTS)

        self.worker = None
        self.export_running = False
        self.log_queue = queue.Queue()

        self._create_style()
        self._create_variables()
        self._create_ui()
        self._load_saved_values()
        self._refresh_models()
        self._update_dependency_status()

        self.after(100, self._process_log_queue)
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    def _create_style(self):
        style = ttk.Style(self)

        try:
            style.theme_use("clam")
        except Exception:
            pass

        style.configure(
            "TFrame",
            background="#101216",
        )

        style.configure(
            "Card.TFrame",
            background="#171A20",
        )

        style.configure(
            "TLabel",
            background="#101216",
            foreground="#E8EAF0",
            font=("Segoe UI", 10),
        )

        style.configure(
            "Title.TLabel",
            background="#101216",
            foreground="#F4F6FA",
            font=("Segoe UI", 22, "bold"),
        )

        style.configure(
            "Subtitle.TLabel",
            background="#101216",
            foreground="#9298A5",
            font=("Segoe UI", 10),
        )

        style.configure(
            "TButton",
            font=("Segoe UI", 10),
            padding=(12, 7),
        )

        style.configure(
            "Accent.TButton",
            font=("Segoe UI", 10, "bold"),
            padding=(16, 9),
        )

        style.configure(
            "TCheckbutton",
            background="#171A20",
            foreground="#E8EAF0",
            font=("Segoe UI", 10),
        )

        style.configure(
            "TCombobox",
            padding=5,
            font=("Segoe UI", 10),
        )

        style.configure(
            "TEntry",
            padding=5,
            font=("Segoe UI", 10),
        )

        style.configure(
            "TLabelframe",
            background="#171A20",
            foreground="#FFFFFF",
        )

        style.configure(
            "TLabelframe.Label",
            background="#171A20",
            foreground="#FFFFFF",
            font=("Segoe UI", 10, "bold"),
        )

    def _create_variables(self):
        self.model_var = tk.StringVar()
        self.output_dir_var = tk.StringVar()
        self.output_name_var = tk.StringVar()

        self.imgsz_var = tk.StringVar(value="320")
        self.opset_var = tk.StringVar(value="12")
        self.device_var = tk.StringVar(value="cpu")
        self.workers_var = tk.StringVar(value="1")
        self.batch_var = tk.StringVar(value="1")

        self.simplify_var = tk.BooleanVar(value=True)
        self.dynamic_var = tk.BooleanVar(value=False)
        self.half_var = tk.BooleanVar(value=False)

        self.status_var = tk.StringVar(value="Готово.")
        self.model_status_var = tk.StringVar(
            value="Модель не выбрана."
        )
        self.dependency_status_var = tk.StringVar(
            value="Проверка зависимостей..."
        )

    def _create_ui(self):
        outer = ttk.Frame(self)
        outer.pack(fill="both", expand=True, padx=18, pady=16)

        header = ttk.Frame(outer)
        header.pack(fill="x", pady=(0, 14))

        ttk.Label(
            header,
            text="P-ESP",
            style="Title.TLabel",
        ).pack(side="left")

        ttk.Label(
            header,
            text="  YOLO → ONNX  •  Центр конвертации",
            style="Subtitle.TLabel",
        ).pack(side="left", pady=(9, 0))

        ttk.Label(
            header,
            textvariable=self.status_var,
            style="Subtitle.TLabel",
        ).pack(side="right", pady=(9, 0))

        main = ttk.Frame(outer)
        main.pack(fill="both", expand=True)

        left = ttk.Frame(
            main,
            style="Card.TFrame",
            padding=16,
        )
        left.pack(
            side="left",
            fill="both",
            expand=True,
            padx=(0, 8),
        )

        right = ttk.Frame(
            main,
            style="Card.TFrame",
            padding=16,
        )
        right.pack(
            side="right",
            fill="both",
            expand=True,
            padx=(8, 0),
        )

        self._build_model_section(left)
        self._build_export_section(left)
        self._build_options_section(left)

        self._build_diagnostics_section(right)
        self._build_log_section(right)
        self._build_buttons(right)

    def _build_model_section(self, parent):
        frame = ttk.LabelFrame(
            parent,
            text="1. Исходная модель",
            padding=12,
        )
        frame.pack(fill="x", pady=(0, 12))

        row = ttk.Frame(frame)
        row.pack(fill="x")

        self.model_combo = ttk.Combobox(
            row,
            textvariable=self.model_var,
            state="readonly",
        )
        self.model_combo.pack(
            side="left",
            fill="x",
            expand=True,
        )

        ttk.Button(
            row,
            text="Выбрать файл",
            command=self._choose_model,
        ).pack(side="left", padx=(8, 0))

        ttk.Button(
            row,
            text="Обновить",
            command=self._refresh_models,
        ).pack(side="left", padx=(8, 0))

        ttk.Label(
            frame,
            textvariable=self.model_status_var,
        ).pack(anchor="w", pady=(8, 0))

    def _build_export_section(self, parent):
        frame = ttk.LabelFrame(
            parent,
            text="2. Куда сохранить ONNX",
            padding=12,
        )
        frame.pack(fill="x", pady=(0, 12))

        ttk.Label(frame, text="Папка:").pack(anchor="w")

        row = ttk.Frame(frame)
        row.pack(fill="x", pady=(3, 8))

        ttk.Entry(
            row,
            textvariable=self.output_dir_var,
        ).pack(
            side="left",
            fill="x",
            expand=True,
        )

        ttk.Button(
            row,
            text="Выбрать",
            command=self._choose_output_dir,
        ).pack(side="left", padx=(8, 0))

        ttk.Label(
            frame,
            text="Имя файла без .onnx:",
        ).pack(anchor="w")

        ttk.Entry(
            frame,
            textvariable=self.output_name_var,
        ).pack(fill="x", pady=(3, 0))

    def _build_options_section(self, parent):
        frame = ttk.LabelFrame(
            parent,
            text="3. Параметры экспорта",
            padding=12,
        )
        frame.pack(fill="x", pady=(0, 12))

        grid = ttk.Frame(frame)
        grid.pack(fill="x")

        ttk.Label(
            grid,
            text="Размер входа:",
        ).grid(row=0, column=0, sticky="w", pady=4)

        ttk.Combobox(
            grid,
            textvariable=self.imgsz_var,
            values=("320", "416", "512", "640", "960"),
            state="readonly",
            width=12,
        ).grid(row=0, column=1, sticky="w", padx=8)

        ttk.Label(
            grid,
            text="OPSET:",
        ).grid(row=1, column=0, sticky="w", pady=4)

        ttk.Combobox(
            grid,
            textvariable=self.opset_var,
            values=("11", "12", "13", "14", "15", "16", "17"),
            state="readonly",
            width=12,
        ).grid(row=1, column=1, sticky="w", padx=8)

        ttk.Label(
            grid,
            text="Устройство:",
        ).grid(row=2, column=0, sticky="w", pady=4)

        ttk.Combobox(
            grid,
            textvariable=self.device_var,
            values=("cpu", "0"),
            state="readonly",
            width=12,
        ).grid(row=2, column=1, sticky="w", padx=8)

        ttk.Label(
            grid,
            text="Workers:",
        ).grid(row=3, column=0, sticky="w", pady=4)

        ttk.Spinbox(
            grid,
            from_=1,
            to=16,
            textvariable=self.workers_var,
            width=12,
        ).grid(row=3, column=1, sticky="w", padx=8)

        ttk.Label(
            grid,
            text="Batch:",
        ).grid(row=4, column=0, sticky="w", pady=4)

        ttk.Spinbox(
            grid,
            from_=1,
            to=16,
            textvariable=self.batch_var,
            width=12,
        ).grid(row=4, column=1, sticky="w", padx=8)

        ttk.Checkbutton(
            frame,
            text="Упростить ONNX (simplify)",
            variable=self.simplify_var,
        ).pack(anchor="w", pady=(10, 2))

        ttk.Checkbutton(
            frame,
            text="Динамический размер входа (dynamic)",
            variable=self.dynamic_var,
        ).pack(anchor="w", pady=2)

        ttk.Checkbutton(
            frame,
            text="FP16 / half",
            variable=self.half_var,
        ).pack(anchor="w", pady=2)

        ttk.Label(
            frame,
            text=(
                "Для старого CPU разумный стартовый профиль: "
                "320 × 320, CPU, batch 1, simplify."
            ),
        ).pack(anchor="w", pady=(10, 0))

    def _build_diagnostics_section(self, parent):
        frame = ttk.LabelFrame(
            parent,
            text="Диагностика",
            padding=12,
        )
        frame.pack(fill="x", pady=(0, 12))

        python_path = find_python()

        ttk.Label(
            frame,
            text=f"Python: {python_path}",
        ).pack(anchor="w", pady=2)

        ttk.Label(
            frame,
            textvariable=self.dependency_status_var,
            wraplength=420,
        ).pack(anchor="w", pady=2)

        ttk.Button(
            frame,
            text="Проверить зависимости",
            command=self._update_dependency_status,
        ).pack(anchor="w", pady=(8, 0))

    def _build_log_section(self, parent):
        frame = ttk.LabelFrame(
            parent,
            text="Журнал",
            padding=8,
        )
        frame.pack(
            fill="both",
            expand=True,
            pady=(0, 12),
        )

        text_frame = ttk.Frame(frame)
        text_frame.pack(fill="both", expand=True)

        self.log_text = tk.Text(
            text_frame,
            bg="#0C0E12",
            fg="#D7DCE5",
            insertbackground="#FFFFFF",
            selectbackground="#2A4A70",
            relief="flat",
            borderwidth=0,
            font=("Consolas", 9),
            wrap="word",
        )
        self.log_text.pack(
            side="left",
            fill="both",
            expand=True,
        )

        scroll = ttk.Scrollbar(
            text_frame,
            orient="vertical",
            command=self.log_text.yview,
        )
        scroll.pack(side="right", fill="y")

        self.log_text.configure(
            yscrollcommand=scroll.set
        )

    def _build_buttons(self, parent):
        frame = ttk.Frame(parent)
        frame.pack(fill="x")

        self.check_button = ttk.Button(
            frame,
            text="Проверить модель",
            command=self._check_model,
        )
        self.check_button.pack(side="left")

        self.export_button = ttk.Button(
            frame,
            text="▶  НАЧАТЬ КОНВЕРТАЦИЮ",
            style="Accent.TButton",
            command=self._start_export,
        )
        self.export_button.pack(
            side="left",
            padx=8,
        )

        self.stop_button = ttk.Button(
            frame,
            text="■  Остановить",
            command=self._stop_export,
            state="disabled",
        )
        self.stop_button.pack(side="left")

        ttk.Button(
            frame,
            text="Открыть папку",
            command=self._open_output_dir,
        ).pack(side="right")

    def _load_saved_values(self):
        self.model_var.set(
            str(self.settings.get("model", ""))
        )

        self.output_dir_var.set(
            str(
                self.settings.get(
                    "output_dir",
                    BASE_DIR / "models",
                )
            )
        )

        self.output_name_var.set(
            str(
                self.settings.get(
                    "output_name",
                    "",
                )
            )
        )

        self.imgsz_var.set(
            str(self.settings.get("imgsz", 320))
        )

        self.opset_var.set(
            str(self.settings.get("opset", 12))
        )

        self.device_var.set(
            str(self.settings.get("device", "cpu"))
        )

        self.workers_var.set(
            str(self.settings.get("workers", 1))
        )

        self.batch_var.set(
            str(self.settings.get("batch", 1))
        )

        self.simplify_var.set(
            bool(self.settings.get("simplify", True))
        )

        self.dynamic_var.set(
            bool(self.settings.get("dynamic", False))
        )

        self.half_var.set(
            bool(self.settings.get("half", False))
        )

    def _collect_settings(self):
        return {
            "model": self.model_var.get().strip(),
            "output_dir": self.output_dir_var.get().strip(),
            "output_name": self.output_name_var.get().strip(),
            "imgsz": int(self.imgsz_var.get()),
            "opset": int(self.opset_var.get()),
            "device": self.device_var.get().strip(),
            "workers": int(self.workers_var.get()),
            "batch": int(self.batch_var.get()),
            "simplify": bool(self.simplify_var.get()),
            "dynamic": bool(self.dynamic_var.get()),
            "half": bool(self.half_var.get()),
        }

    def _refresh_models(self):
        models = discover_models()
        values = [str(p) for p in models]
        current = self.model_var.get().strip()

        if current and current not in values:
            values.insert(0, current)

        self.model_combo["values"] = values

        if current in values:
            self.model_combo.set(current)
        elif values:
            self.model_combo.set(values[0])
            self.model_var.set(values[0])
        else:
            self.model_combo.set("")
            self.model_var.set("")

        if values:
            self._set_status(
                f"Найдено моделей: {len(values)}"
            )
        else:
            self._set_status(
                "Модели .pt/.pth не найдены."
            )

        self._update_model_status()

    def _choose_model(self):
        path = filedialog.askopenfilename(
            title="Выберите YOLO-модель",
            initialdir=str(BASE_DIR),
            filetypes=[
                ("YOLO / PyTorch", "*.pt *.pth"),
                ("PT", "*.pt"),
                ("PTH", "*.pth"),
                ("Все файлы", "*.*"),
            ],
        )

        if not path:
            return

        self.model_var.set(path)

        if not self.output_name_var.get().strip():
            self.output_name_var.set(
                Path(path).stem
            )

        self._update_model_status()
        self._save_settings()
        self._log(
            f"Выбрана модель: {path}"
        )

    def _choose_output_dir(self):
        path = filedialog.askdirectory(
            title="Выберите папку для ONNX",
            initialdir=(
                self.output_dir_var.get()
                or str(BASE_DIR)
            ),
        )

        if path:
            self.output_dir_var.set(path)
            self._save_settings()
            self._log(
                f"Папка результата: {path}"
            )

    def _update_model_status(self):
        path_text = self.model_var.get().strip()

        if not path_text:
            self.model_status_var.set(
                "Модель не выбрана."
            )
            return

        path = Path(path_text)

        if not path.is_file():
            self.model_status_var.set(
                "НЕТ ФАЙЛА"
            )
            return

        try:
            size = pretty_size(
                path.stat().st_size
            )
        except Exception:
            size = "размер неизвестен"

        self.model_status_var.set(
            f"ГОТОВА  •  {size}  •  {path.name}"
        )

    def _update_dependency_status(self):
        self.dependency_status_var.set(
            "Проверка зависимостей..."
        )
        self.after(
            50,
            self._dependency_check_worker,
        )

    def _dependency_check_worker(self):
        required = [
            ("ultralytics", "ultralytics"),
            ("onnx", "onnx"),
            ("onnxruntime", "onnxruntime"),
        ]

        missing = []
        versions = []

        for import_name, package_name in required:
            try:
                module = __import__(import_name)
                version = getattr(
                    module,
                    "__version__",
                    "?",
                )
                versions.append(
                    f"{package_name} {version}"
                )
            except Exception:
                missing.append(package_name)

        if missing:
            text = (
                "Не хватает: "
                + ", ".join(missing)
            )
        else:
            text = (
                "Готово: "
                + ", ".join(versions)
            )

        self.dependency_status_var.set(text)

    def _check_model(self):
        model_text = self.model_var.get().strip()

        if not model_text:
            messagebox.showwarning(
                "Проверка модели",
                "Сначала выберите .pt или .pth модель.",
            )
            return

        path = Path(model_text)

        if not path.is_file():
            messagebox.showerror(
                "Проверка модели",
                "Файл модели не найден.",
            )
            return

        self._log("=" * 60)
        self._log("ПРОВЕРКА МОДЕЛИ")
        self._log(f"Файл: {path}")
        self._log(
            f"Размер: {pretty_size(path.stat().st_size)}"
        )

        threading.Thread(
            target=self._check_model_worker,
            args=(path,),
            daemon=True,
        ).start()

    def _check_model_worker(self, path):
        python = find_python()

        source = (
            "import sys\n"
            "try:\n"
            "    from ultralytics import YOLO\n"
            f"    m = YOLO(r'''{str(path)}''')\n"
            "    print('MODEL_OK')\n"
            "    print('NAMES=', getattr(m, 'names', None))\n"
            "except Exception as e:\n"
            "    print('MODEL_ERROR=', repr(e))\n"
            "    sys.exit(2)\n"
        )

        try:
            result = subprocess.run(
                [str(python), "-c", source],
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=120,
                creationflags=getattr(
                    subprocess,
                    "CREATE_NO_WINDOW",
                    0,
                ),
            )

            for line in result.stdout.splitlines():
                self._log(line)

            if result.returncode == 0:
                self._set_status(
                    "Модель успешно прочитана."
                )
            else:
                self._set_status(
                    "Ошибка проверки модели."
                )

        except subprocess.TimeoutExpired:
            self._log(
                "Проверка превысила 120 секунд."
            )
            self._set_status(
                "Проверка превысила время."
            )
        except Exception:
            self._log(traceback.format_exc())
            self._set_status(
                "Ошибка проверки."
            )

    def _build_export_command(self):
        model = Path(
            self.model_var.get().strip()
        )

        output_dir = Path(
            self.output_dir_var.get().strip()
        )

        if not output_dir:
            output_dir = BASE_DIR / "models"

        output_dir.mkdir(
            parents=True,
            exist_ok=True,
        )

        output_name = (
            self.output_name_var.get().strip()
        )

        if not output_name:
            output_name = model.stem

        if output_name.lower().endswith(".onnx"):
            output_name = output_name[:-5]

        imgsz = int(self.imgsz_var.get())
        opset = int(self.opset_var.get())
        workers = int(self.workers_var.get())
        batch = int(self.batch_var.get())
        device = self.device_var.get().strip()

        if imgsz not in {
            320,
            416,
            512,
            640,
            960,
        }:
            raise ValueError(
                "Недопустимый размер входа."
            )

        if not 11 <= opset <= 17:
            raise ValueError(
                "OPSET должен быть от 11 до 17."
            )

        if workers < 1:
            raise ValueError(
                "Workers должен быть >= 1."
            )

        if batch < 1:
            raise ValueError(
                "Batch должен быть >= 1."
            )

        python = find_python()

        export_code = (
            "from ultralytics import YOLO; "
            f"m=YOLO(r'''{str(model)}'''); "
            f"m.export("
            f"format='onnx', "
            f"imgsz={imgsz}, "
            f"opset={opset}, "
            f"simplify={bool(self.simplify_var.get())}, "
            f"dynamic={bool(self.dynamic_var.get())}, "
            f"half={bool(self.half_var.get())}, "
            f"device={device!r}, "
            f"workers={workers}, "
            f"batch={batch}, "
            f"project=r'''{str(output_dir)}''', "
            f"name={output_name!r}, "
            f"exist_ok=True"
            f")"
        )

        command = [
            str(python),
            "-c",
            export_code,
        ]

        return (
            command,
            output_dir / f"{output_name}.onnx",
        )

    def _start_export(self):
        if self.export_running:
            return

        model_text = self.model_var.get().strip()

        if not model_text:
            messagebox.showwarning(
                "Конвертация",
                "Выберите исходную .pt/.pth модель.",
            )
            return

        model = Path(model_text)

        if not model.is_file():
            messagebox.showerror(
                "Конвертация",
                "Исходная модель не найдена.",
            )
            return

        try:
            command, expected_output = (
                self._build_export_command()
            )
        except Exception as exc:
            messagebox.showerror(
                "Параметры",
                str(exc),
            )
            return

        self._save_settings()

        self._log("")
        self._log("=" * 70)
        self._log("НАЧАЛО КОНВЕРТАЦИИ")
        self._log(f"Источник: {model}")
        self._log(
            f"Ожидаемый ONNX: {expected_output}"
        )
        self._log("=" * 70)

        self.export_running = True

        self.export_button.configure(
            state="disabled"
        )
        self.check_button.configure(
            state="disabled"
        )
        self.stop_button.configure(
            state="normal"
        )

        self.status_var.set(
            "Экспорт выполняется..."
        )

        self.worker = ExportWorker(
            command,
            self._log,
            self._export_done,
        )
        self.worker.start()

    def _stop_export(self):
        if self.worker is not None:
            self._log(
                "Запрошена остановка..."
            )
            self.worker.stop()

    def _export_done(self, return_code):
        self.after(
            0,
            lambda: self._finish_export(
                return_code
            ),
        )

    def _finish_export(self, return_code):
        self.export_running = False

        self.export_button.configure(
            state="normal"
        )
        self.check_button.configure(
            state="normal"
        )
        self.stop_button.configure(
            state="disabled"
        )

        model_text = self.model_var.get().strip()
        model = (
            Path(model_text)
            if model_text
            else None
        )

        output_dir = Path(
            self.output_dir_var.get().strip()
            or str(BASE_DIR / "models")
        )

        name = (
            self.output_name_var.get().strip()
        )

        if not name and model:
            name = model.stem

        if name.lower().endswith(".onnx"):
            name = name[:-5]

        result_file = (
            output_dir / f"{name}.onnx"
        )

        if return_code == 0 and result_file.is_file():
            try:
                size = pretty_size(
                    result_file.stat().st_size
                )
            except Exception:
                size = "неизвестно"

            self.status_var.set(
                "Готово: ONNX создан."
            )

            self._log("")
            self._log(
                f"ГОТОВО: {result_file}"
            )
            self._log(
                f"Размер: {size}"
            )

            self._validate_onnx_async(
                result_file
            )

            messagebox.showinfo(
                "Конвертация завершена",
                "ONNX успешно создан:\n\n"
                f"{result_file}\n\n"
                f"Размер: {size}",
            )
        else:
            self.status_var.set(
                "Экспорт завершился с ошибкой."
            )

            self._log("")
            self._log(
                "ONNX не найден или экспорт завершился с ошибкой."
            )

    def _validate_onnx_async(self, path):
        threading.Thread(
            target=self._validate_onnx_worker,
            args=(path,),
            daemon=True,
        ).start()

    def _validate_onnx_worker(self, path):
        python = find_python()

        source = (
            "import onnx\n"
            f"p=r'''{str(path)}'''\n"
            "m=onnx.load(p)\n"
            "onnx.checker.check_model(m)\n"
            "print('ONNX_CHECK_OK')\n"
            "print('INPUTS=', [x.name for x in m.graph.input])\n"
            "print('OUTPUTS=', [x.name for x in m.graph.output])\n"
        )

        try:
            result = subprocess.run(
                [str(python), "-c", source],
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=120,
                creationflags=getattr(
                    subprocess,
                    "CREATE_NO_WINDOW",
                    0,
                ),
            )

            for line in result.stdout.splitlines():
                self._log(
                    "ПРОВЕРКА: " + line
                )

            if result.returncode == 0:
                self._set_status(
                    "ONNX проверен."
                )
            else:
                self._set_status(
                    "ONNX создан, но проверка не прошла."
                )

        except Exception:
            self._log(traceback.format_exc())

    def _open_output_dir(self):
        path = Path(
            self.output_dir_var.get().strip()
            or str(BASE_DIR)
        )

        try:
            path.mkdir(
                parents=True,
                exist_ok=True,
            )
            os.startfile(str(path))
        except Exception as exc:
            messagebox.showerror(
                "Ошибка",
                f"Не удалось открыть папку:\n{exc}",
            )

    def _save_settings(self):
        try:
            safe_save_json(
                SETTINGS_FILE,
                self._collect_settings(),
            )
        except Exception:
            pass

    def _log(self, text):
        self.log_queue.put(str(text))

    def _process_log_queue(self):
        try:
            while True:
                text = self.log_queue.get_nowait()
                self.log_text.insert(
                    "end",
                    text + "\n",
                )
                self.log_text.see("end")
        except queue.Empty:
            pass

        self.after(
            100,
            self._process_log_queue,
        )

    def _set_status(self, text):
        self.after(
            0,
            lambda: self.status_var.set(
                str(text)
            ),
        )

    def _on_close(self):
        self._save_settings()

        if (
            self.worker is not None
            and self.export_running
        ):
            answer = messagebox.askyesno(
                "Экспорт выполняется",
                "Экспорт ещё выполняется.\n\n"
                "Остановить его и закрыть программу?",
            )

            if not answer:
                return

            try:
                self.worker.stop()
            except Exception:
                pass

        self.destroy()


def main():
    app = App()
    app.mainloop()


if __name__ == "__main__":
    main()
