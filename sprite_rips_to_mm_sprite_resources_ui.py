import copy
import json
import os
import shutil
import subprocess
import sys
import tkinter as tk
import tkinter.font as tkfont
from pathlib import Path
from tkinter import messagebox, ttk
import webbrowser
DEFAULT_SUBJECT_CONFIG = {
    "resize_to_percent": 100.0,
    "background_color": "#00FF00",
    "color_threshold": 100,
    "remove_background": True,
    "crop_sprites": True,
    "sheet": {"width": None, "height": None}
}
DEFAULT_ANIMATION_CONFIG = {
    "regenerate": True,
    "delay": 1,
    "offset": {"x": 0.0, "y": 0.0},
    "recover_cropped_offset": {"x": True, "y": True},
}
ASSET_BUNDLE_DIR = "assets"
GAME_THEME_CONFIG_FILENAME = "config.json"
DEFAULT_GAME_THEME_CONFIG = {"subject": None, "is_hd": True}
def resolve_storage_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent
def resolve_asset_source() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys._MEIPASS) / ASSET_BUNDLE_DIR
    return Path(__file__).resolve().parent / ASSET_BUNDLE_DIR
class ConfigManagerUI(tk.Tk):
    def __init__(self) -> None:
        super().__init__()

        self.bind("<FocusIn>", self._on_window_focus_in, add="+")

        self.title("Frames to MM sprite resources")
        
        self.minsize(705, 490)
        self.root_dir = resolve_storage_root()
        self.bundle_assets_dir = resolve_asset_source()
  
        icon_path = self.bundle_assets_dir / "icon.ico"
        if icon_path.exists():
            try:
                self.icon_path = str(icon_path)
                self.iconbitmap(self.icon_path)
            except Exception:
                self.icon_path = None
        self.root_config_path = self.root_dir / "config.json"
        self.root_config = self._read_json(self.root_config_path, {"subject": None, "game_theme": None, "is_hd": True, "reduce_file_size": False})
        if not isinstance(self.root_config, dict):
            self.root_config = {"subject": None, "game_theme": None, "is_hd": True, "reduce_file_size": False}
        self._root_config_has_reduce = "reduce_file_size" in self.root_config
        self.root_config.setdefault("subject", None)
        self.root_config.setdefault("game_theme", None)
        self.root_config.setdefault("reduce_file_size", False)
        self.root_config.setdefault("is_hd", True)
        self.subject_config_path = None
        self.subject_config_data = {}
        self.animation_data = {}
        self.animation_names = []
        self.current_subject_name = None
        self.current_animation = None
        self.subject_store = {}
        self.subject_options = []
        self.game_theme_options = []
        self.theme_config_cache = {}
        self._none_subject_memory = self.root_config.get("subject") or None
        raw_is_hd = self.root_config.get("is_hd")
        self._none_is_hd_memory = self._normalize_is_hd_value(raw_is_hd)
        initial_game_theme = self.root_config.get("game_theme")
        self.current_game_theme = initial_game_theme if initial_game_theme else None
        self.game_theme_var = tk.StringVar(value=self._format_game_theme_value(initial_game_theme))
        initial_hd_value = self._resolve_is_hd_for_theme(self.current_game_theme)
        self.is_hd_theme_var = tk.BooleanVar(value=initial_hd_value)
        self._sync_root_subject_field()
        self.subject_var = tk.StringVar()
        self.reduce_file_size_var = tk.BooleanVar(value=bool(self.root_config.get("reduce_file_size", False)))
        self._integer_validate_callback = None
        self._signed_integer_validate_callback = None
        self._is_setting_background_color = False
        self._last_valid_background_color = ''
        self.color_preview = None
        self._color_preview_rect = None
        self._default_color_preview_fill = ''
        self.offset_group = None
        self.offset_x_entry = None
        self.offset_y_entry = None
        self.recover_group = None
        self.recover_x_check = None
        self.recover_y_check = None
        self._build_ui()
        self.populate_game_theme_options()
        self._initialize_selection()
        self.notebook.bind("<<NotebookTabChanged>>", self.on_notebook_tab_changed)

    def _on_window_focus_in(self, event: tk.Event) -> None:
        self.reload_subjects()

    def _ensure_runtime_assets(self) -> None:
        if not getattr(sys, "frozen", False):
            return
        source = self.bundle_assets_dir
        if not source.exists():
            return
        for entry in source.iterdir():
            target = self.root_dir / entry.name
            if target.exists():
                continue
            try:
                if entry.is_dir():
                    shutil.copytree(entry, target)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(entry, target)
            except OSError as exc:
                print(f"Warning: could not copy bundled asset {entry.name}: {exc}", file=sys.stderr)
    def _build_ui(self) -> None:
        outer_padding = 12
        section_padding = 10
        column_gap = outer_padding // 2
        self.subject_entries = []
        self.animation_form_widgets = []
        self._animation_form_enabled = False
        self._integer_validate_callback = self.register(self._validate_integer_input)
        self._signed_integer_validate_callback = self.register(self._validate_signed_integer_input)
        subject_header = ttk.Frame(self)
        subject_header.pack(fill="x", padx=outer_padding, pady=(outer_padding, 0))
        subject_header.columnconfigure(1, weight=1)
        subject_header.columnconfigure(4, weight=1)
        subject_header.columnconfigure(2, weight=0)
        subject_header.columnconfigure(5, weight=0)
        subject_header.columnconfigure(6, weight=0)
        ttk.Label(subject_header, text="Game Theme:").grid(row=0, column=0, sticky="w")
        self.game_theme_combo = ttk.Combobox(subject_header, textvariable=self.game_theme_var, state="readonly")
        self.game_theme_combo.grid(row=0, column=1, sticky="ew", padx=(6, 6))
        self.is_hd_check = ttk.Checkbutton(subject_header, text="is HD", variable=self.is_hd_theme_var, command=self.on_hd_theme_toggle)
        self.is_hd_check.grid(row=0, column=2, sticky="w", padx=(6, 6))
        self.game_theme_combo.bind("<<ComboboxSelected>>", self.on_game_theme_change)
        ttk.Label(subject_header, text="Subject:").grid(row=0, column=3, sticky="w")
        self.subject_combo = ttk.Combobox(subject_header, textvariable=self.subject_var, state="readonly")
        self.subject_combo.grid(row=0, column=4, sticky="ew", padx=(6, 0))
        self.subject_combo.bind("<<ComboboxSelected>>", self.on_subject_change)
        self.reload_button = ttk.Button(subject_header, text="Refresh", command=self.reload_subjects)
        self.reload_button.grid(row=0, column=5, sticky="w", padx=(8, 0))
        self.help_button = ttk.Button(subject_header, text="?", width=3, command=self.show_about)
        self.help_button.grid(row=0, column=6, sticky="e", padx=(8, 0))
        self.notebook = ttk.Notebook(self)
        self.notebook.pack(fill="both", expand=True, padx=outer_padding, pady=(outer_padding, outer_padding))
        self.subject_tab = ttk.Frame(self.notebook)
        self.animations_tab = ttk.Frame(self.notebook)
        self.notebook.add(self.subject_tab, text="Subject")
        self.notebook.add(self.animations_tab, text="Animations")
        subject_frame = ttk.Frame(self.subject_tab, padding=outer_padding)
        subject_frame.pack(fill="both", expand=True)
        subject_groups = ttk.Frame(subject_frame)
        subject_groups.pack(fill="both", expand=True)
        subject_groups.columnconfigure(0, weight=1, uniform="subject_cols")
        subject_groups.columnconfigure(1, weight=1, uniform="subject_cols")
        subject_groups.rowconfigure(0, weight=1)
        processing_group = ttk.LabelFrame(subject_groups, text="Processing", padding=section_padding)
        processing_group.grid(row=0, column=0, sticky="nsew", padx=(0, column_gap))
        processing_group.columnconfigure(1, weight=1)
        processing_group.columnconfigure(2, weight=0)
        self.resize_var = tk.StringVar()
        ttk.Label(processing_group, text="Resize to percent").grid(row=0, column=0, sticky="w", pady=4)
        resize_entry = ttk.Entry(processing_group, textvariable=self.resize_var)
        resize_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0), pady=4, columnspan=2)
        resize_entry.configure(validate="key", validatecommand=(self._integer_validate_callback, "%P"))
        self.subject_entries.append(resize_entry)
        self.color_remove_var = tk.StringVar()
        ttk.Label(processing_group, text="Background color").grid(row=1, column=0, sticky="w", pady=4)
        color_entry = ttk.Entry(processing_group, textvariable=self.color_remove_var)
        color_entry.grid(row=1, column=1, sticky="ew", padx=(8, 0), pady=4)
        color_entry.bind("<FocusOut>", self._on_background_color_focus_out)
        self.color_remove_var.trace_add("write", self._on_background_color_change)
        self.color_preview = tk.Canvas(processing_group, width=20, height=20, highlightthickness=1, highlightbackground="#b3b3b3")
        self._default_color_preview_fill = self.color_preview.cget("background")
        self._color_preview_rect = self.color_preview.create_rectangle(0, 0, 20, 20, outline="", fill=self._default_color_preview_fill)
        self.color_preview.grid(row=1, column=2, padx=(0, 0), pady=4, sticky="ew")
        self._update_background_color_preview(None)
        self.subject_entries.append(color_entry)
        ttk.Label(processing_group, text="If already transparent, leave it blank", foreground="gray",).grid(row=2, column=0, columnspan=3, sticky="w", pady=(0, 4))
        self.color_threshold_var = tk.StringVar()
        ttk.Label(processing_group, text="Color threshold").grid(row=3, column=0, sticky="w", pady=4)
        threshold_entry = ttk.Entry(processing_group, textvariable=self.color_threshold_var)
        threshold_entry.grid(row=3, column=1, sticky="ew", padx=(8, 0), pady=4, columnspan=2)
        threshold_entry.configure(validate="key", validatecommand=(self._integer_validate_callback, "%P"))
        self.subject_entries.append(threshold_entry)
        # New boolean options: remove background and crop sprites
        self.remove_background_var = tk.BooleanVar(value=True)
        remove_bg_check = ttk.Checkbutton(
            processing_group,
            text="Remove background",
            variable=self.remove_background_var,
        )
        remove_bg_check.grid(row=4, column=0, columnspan=3, sticky="w", pady=6)
        self.subject_entries.append(remove_bg_check)
        self.crop_sprites_var = tk.BooleanVar(value=True)
        crop_check = ttk.Checkbutton(
            processing_group,
            text="Crop sprites",
            variable=self.crop_sprites_var,
        )
        crop_check.grid(row=5, column=0, columnspan=3, sticky="w", pady=(0,6))
        self.subject_entries.append(crop_check)
        ttk.Label(processing_group, text="Cropping reduces file size", foreground="gray",).grid(row=6, column=0, columnspan=2, sticky="w", pady=(0,0))
        ttk.Label(processing_group, text="Cropping is based on the bg color and threshold", foreground="gray",).grid(row=7, column=0, columnspan=3, sticky="w", pady=(0, 4))
        sheet_group = ttk.LabelFrame(subject_groups, text="Sheet dimensions", padding=section_padding)
        sheet_group.grid(row=0, column=1, sticky="nsew", padx=(column_gap, 0))
        sheet_group.columnconfigure(1, weight=1)
        self.sheet_width_var = tk.StringVar()
        ttk.Label(sheet_group, text="Width").grid(row=0, column=0, sticky="w", pady=4)
        sheet_width_entry = ttk.Entry(sheet_group, textvariable=self.sheet_width_var)
        sheet_width_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0), pady=4)
        sheet_width_entry.configure(validate="key", validatecommand=(self._integer_validate_callback, "%P"))
        self.subject_entries.append(sheet_width_entry)
        self.sheet_height_var = tk.StringVar()
        ttk.Label(sheet_group, text="Height").grid(row=1, column=0, sticky="w", pady=4)
        sheet_height_entry = ttk.Entry(sheet_group, textvariable=self.sheet_height_var)
        sheet_height_entry.grid(row=1, column=1, sticky="ew", padx=(8, 0), pady=4)
        sheet_height_entry.configure(validate="key", validatecommand=(self._integer_validate_callback, "%P"))
        self.subject_entries.append(sheet_height_entry)
        ttk.Label(sheet_group, text="For automatic sizing, leave it blank", foreground="gray",).grid(
            row=2, column=0, columnspan=2, sticky="w", pady=(4, 0)
        )
        animations_frame = ttk.Frame(self.animations_tab, padding=outer_padding)
        animations_frame.pack(fill="both", expand=True)
        list_container = ttk.Frame(animations_frame, padding=section_padding)
        list_container.pack(side="left", fill="y")
        list_frame = ttk.Frame(list_container)
        list_frame.pack(fill="both", expand=True)
        self.animation_listbox = tk.Listbox(list_frame, exportselection=False, height=15)
        self.animation_listbox.pack(side="left", fill="both", expand=True)
        scrollbar = ttk.Scrollbar(list_frame, orient="vertical", command=self.animation_listbox.yview)
        scrollbar.pack(side="right", fill="y")
        self.animation_listbox.configure(yscrollcommand=scrollbar.set)
        self.animation_listbox.bind("<<ListboxSelect>>", self.on_animation_selected)
        self.reload_animations_button = ttk.Button(
            list_container,
            text="Refresh",
            command=self.reload_animation_directories,
        )
        self.reload_animations_button.pack(fill="x", pady=(8, 0))
        self.toggle_regenerate_button = ttk.Button(
            list_container,
            text="Toggle Regenerate",
            command=self.toggle_all_animation_regenerate,
        )
        self.toggle_regenerate_button.pack(fill="x", pady=(4, 0))
        detail_frame = ttk.LabelFrame(animations_frame, text="Selected animation", padding=section_padding)
        detail_frame.pack(side="left", fill="both", expand=True, padx=(outer_padding, 0))
        detail_frame.columnconfigure(1, weight=1)
        self.anim_rege_var = tk.BooleanVar(value=True)
        regen_check = ttk.Checkbutton(detail_frame, text="Regenerate", variable=self.anim_rege_var, command=self._on_animation_regenerate_change)
        regen_check.grid(row=0, column=0, columnspan=2, sticky="w", pady=4)
        self.animation_form_widgets.append(regen_check)
        ttk.Label(detail_frame, text="If disabled, already generated sprites will be added to the spritesheet.\nEdited offsets and sub-positions will remain unchanged.", foreground="gray",).grid(
            row=1, column=0, columnspan=2, sticky="w", pady=(4, 0)
        )
        self.animation_form_widgets.append(regen_check)
        self.anim_delay_var = tk.StringVar()
        ttk.Label(detail_frame, text="Delay").grid(row=2, column=0, sticky="w", pady=4)
        delay_entry = ttk.Entry(detail_frame, textvariable=self.anim_delay_var)
        delay_entry.grid(row=2, column=1, sticky="ew", padx=(8, 0), pady=4)
        delay_entry.configure(validate="key", validatecommand=(self._integer_validate_callback, "%P"))
        self.animation_form_widgets.append(delay_entry)
        offset_group = ttk.LabelFrame(detail_frame, text="Offset", padding=section_padding)
        offset_group.grid(row=3, column=0, columnspan=2, sticky="ew", pady=(12, 0))
        offset_group.columnconfigure(1, weight=1)
        offset_group.columnconfigure(3, weight=1)
        self.offset_group = offset_group
        self.anim_offset_x_var = tk.StringVar()
        ttk.Label(offset_group, text="X").grid(row=0, column=0, sticky="w", pady=4)
        offset_x_entry = ttk.Entry(offset_group, textvariable=self.anim_offset_x_var)
        offset_x_entry.grid(row=0, column=1, sticky="ew", padx=(8, column_gap), pady=4)
        offset_x_entry.configure(validate="key", validatecommand=(self._signed_integer_validate_callback, "%P"))
        self.offset_x_entry = offset_x_entry
        self.animation_form_widgets.append(offset_x_entry)
        self.anim_offset_y_var = tk.StringVar()
        ttk.Label(offset_group, text="Y").grid(row=0, column=2, sticky="w", pady=4)
        offset_y_entry = ttk.Entry(offset_group, textvariable=self.anim_offset_y_var)
        offset_y_entry.grid(row=0, column=3, sticky="ew", padx=(8, 0), pady=4)
        offset_y_entry.configure(validate="key", validatecommand=(self._signed_integer_validate_callback, "%P"))
        self.offset_y_entry = offset_y_entry
        self.animation_form_widgets.append(offset_y_entry)
        ttk.Label(
            offset_group,
            text="Adjusts the offset for every sprite within the selected animation",
            foreground="gray",
        ).grid(row=1, column=0, columnspan=4, sticky="w", pady=(4, 0))
        self.animation_form_widgets.append(offset_group)
        recover_group = ttk.LabelFrame(detail_frame, text="Recover cropped offset", padding=section_padding)
        recover_group.grid(row=4, column=0, columnspan=2, sticky="ew", pady=(12, 0))
        recover_group.columnconfigure(0, weight=1)
        recover_group.columnconfigure(1, weight=1)
        self.recover_group = recover_group
        self.anim_recover_x_var = tk.BooleanVar(value=True)
        recover_x_check = ttk.Checkbutton(
            recover_group,
            text="X",
            variable=self.anim_recover_x_var,
        )
        recover_x_check.grid(row=0, column=0, sticky="w", pady=4, padx=(0, column_gap))
        self.recover_x_check = recover_x_check
        self.animation_form_widgets.append(recover_x_check)
        self.anim_recover_y_var = tk.BooleanVar(value=True)
        recover_y_check = ttk.Checkbutton(
            recover_group,
            text="Y",
            variable=self.anim_recover_y_var,
        )
        recover_y_check.grid(row=0, column=1, sticky="w", pady=4, padx=(column_gap, 0))
        self.recover_y_check = recover_y_check
        self.animation_form_widgets.append(recover_y_check)
        ttk.Label(
            recover_group,
            text="If enabled, the offsets will be adjusted to account for any cropping that was done.",
            foreground="gray",
        ).grid(row=1, column=0, columnspan=2, sticky="w", pady=(4, 0))
        self.animation_form_widgets.append(recover_group)
        detail_frame.rowconfigure(5, weight=1)
    
        bottom_frame = ttk.Frame(self, padding=(outer_padding, 0, outer_padding, outer_padding))
        bottom_frame.pack(fill="x")
        self.reduce_file_size_check = ttk.Checkbutton(
            bottom_frame,
            text="Reduce file size",
            variable=self.reduce_file_size_var,
        )
        self.reduce_file_size_check.pack(side="left")
        ttk.Label(
            bottom_frame,
            text="If enabled, except a bit slower generation",
            foreground="gray",
        ).pack(side="left", padx=(8, 0))
        self.save_and_generate_button = ttk.Button(
            bottom_frame, text='Save & Generate', command=self.save_and_generate
        )
        self.save_and_generate_button.pack(side="right")
        self.save_button = ttk.Button(bottom_frame, text="Save All", command=self.save_all)
        self.save_button.pack(side="right", padx=(0, 8))
        self.clear_subject_form()
        self.clear_animation_form()
        self.update_animation_form_state(False)
    def populate_game_theme_options(self) -> None:
        themes = self.discover_subjects(None)
        options = ["None"] + themes
        self.game_theme_options = options
        self.game_theme_combo["values"] = options
    def populate_subject_options(self) -> None:
        game_theme = self._get_selected_game_theme()
        subjects = self.discover_subjects(game_theme)
        self.subject_options = subjects
        self.subject_combo["values"] = subjects
    def _initialize_selection(self) -> None:
        options = list(self.game_theme_combo["values"])
        preferred_theme = self._format_game_theme_value(self.root_config.get("game_theme"))
        if options:
            if preferred_theme not in options:
                preferred_theme = options[0]
            self.game_theme_var.set(preferred_theme)
            self.game_theme_combo.set(preferred_theme)
        else:
            preferred_theme = self._format_game_theme_value(None)
            self.game_theme_var.set(preferred_theme)
        self.current_game_theme = self._parse_game_theme_value(preferred_theme)
        self.root_config["game_theme"] = self.current_game_theme
        self.is_hd_theme_var.set(self._resolve_is_hd_for_theme(self.current_game_theme))
        self.populate_subject_options()
        subjects = list(self.subject_combo["values"])
        if not subjects:
            self.subject_var.set("")
            self.subject_combo.set("")
            self.disable_subject_forms()
            self.current_subject_name = None
            if self.current_game_theme:
                self._update_theme_subject(self.current_game_theme, None)
            else:
                self._set_none_subject(None)
            return
        if self.current_game_theme:
            preferred_subject = self._get_theme_selected_subject(self.current_game_theme)
        else:
            preferred_subject = self.root_config.get("subject")
        if preferred_subject in subjects:
            selected_subject = preferred_subject
        else:
            selected_subject = subjects[0]
        self.subject_var.set(selected_subject)
        self.subject_combo.set(selected_subject)
        if self.current_game_theme:
            self._update_theme_subject(self.current_game_theme, selected_subject)
        else:
            self._set_none_subject(selected_subject)
        self.load_subject(selected_subject)
    
    def discover_subjects(self, game_theme=None):
        base_dir = self.root_dir if not game_theme else self.root_dir / game_theme
        if not base_dir.is_dir():
            return []
        subjects = []
        for entry in sorted(base_dir.iterdir(), key=lambda item: item.name.lower()):
            if entry.is_dir() and not entry.name.startswith((".", "_", "assets")):
                subjects.append(entry.name)
        return subjects
    def _format_game_theme_value(self, value):
        return "None" if not value else str(value)
    def _parse_game_theme_value(self, value):
        text = str(value).strip() if value is not None else ""
        if not text or text == "None":
            return None
        return text
    def _get_selected_game_theme(self):
        return self._parse_game_theme_value(self.game_theme_var.get())
    def _resolve_subject_dir(self, subject, game_theme):
        base_dir = self.root_dir if not game_theme else self.root_dir / game_theme
        return base_dir / subject
    def _normalize_is_hd_value(self, value) -> bool:
        return True if value is None else bool(value)

    def _resolve_is_hd_for_theme(self, game_theme):
        if not game_theme:
            return self._none_is_hd_memory
        config = self._get_theme_config(game_theme)
        return self._normalize_is_hd_value(config.get("is_hd"))

    def _set_none_subject(self, value) -> None:
        normalized = value if value else None
        self._none_subject_memory = normalized
        self._sync_root_subject_field()

    def _set_none_is_hd(self, value) -> None:
        self._none_is_hd_memory = self._normalize_is_hd_value(value)

    def _sync_root_subject_field(self) -> None:
        if self.current_game_theme is None:
            self.root_config["subject"] = self._none_subject_memory
            self.root_config["is_hd"] = self._none_is_hd_memory
        else:
            self.root_config["subject"] = None
            self.root_config["is_hd"] = None
    def _build_theme_config_payload(self, game_theme: str) -> dict:
        config = self._get_theme_config(game_theme)
        return {
            "subject": config.get("subject") if config.get("subject") else None,
            "is_hd": self._normalize_is_hd_value(config.get("is_hd")),
        }
    def _subject_store_key(self, game_theme, subject):
        return (game_theme or None, subject)
    def _resolve_theme_config_path(self, game_theme: str) -> Path:
        return self.root_dir / game_theme / GAME_THEME_CONFIG_FILENAME
    def _get_theme_config(self, game_theme: str) -> dict:
        if not game_theme:
            return copy.deepcopy(DEFAULT_GAME_THEME_CONFIG)
        cached = self.theme_config_cache.get(game_theme)
        if cached is None:
            config_path = self._resolve_theme_config_path(game_theme)
            config = self._read_json(config_path, DEFAULT_GAME_THEME_CONFIG)
            if not isinstance(config, dict):
                config = {}
            config = copy.deepcopy(config)
            config.setdefault("subject", None)
            config.setdefault("is_hd", True)
            self.theme_config_cache[game_theme] = config
            return config
        config = copy.deepcopy(cached)
        config.setdefault("subject", None)
        config.setdefault("is_hd", True)
        return config

    def _get_theme_selected_subject(self, game_theme: str):
        if not game_theme:
            return None
        config = self._get_theme_config(game_theme)
        subject = config.get("subject")
        return subject if subject else None

    def _update_theme_subject(self, game_theme: str, subject) -> None:
        if not game_theme:
            return
        config = self._get_theme_config(game_theme)
        value = subject if subject else None
        if config.get("subject") == value:
            return
        config["subject"] = value
        self.theme_config_cache[game_theme] = config

    def _update_theme_is_hd(self, game_theme: str, value: bool) -> None:
        if not game_theme:
            return
        normalized = self._normalize_is_hd_value(value)
        config = self._get_theme_config(game_theme)
        if self._normalize_is_hd_value(config.get("is_hd")) == normalized:
            return
        config["is_hd"] = normalized
        self.theme_config_cache[game_theme] = config

    def load_subject(self, subject: str) -> None:
        game_theme = self._get_selected_game_theme()
        subject_dir = self._resolve_subject_dir(subject, game_theme)
        config_path = subject_dir / "config.json"
        key = self._subject_store_key(game_theme, subject)
        if key in self.subject_store:
            stored = self.subject_store[key]
            self.subject_config_data = copy.deepcopy(stored.get("config_data", {}))
            animations = {}
            for name, info in stored.get("animations", {}).items():
                animations[name] = {"path": info["path"], "data": copy.deepcopy(info["data"])}
            self.animation_data = animations
        else:
            subject_data = self._ensure_subject_defaults(self._read_json(config_path, DEFAULT_SUBJECT_CONFIG))
            self.subject_config_data = subject_data
            self.animation_data = self._load_animation_data(subject_dir / "raw")
        self._sync_reduce_file_size_from_subject()
        self.subject_config_path = config_path
        self.current_subject_name = subject
        self.current_game_theme = game_theme
        self.enable_subject_forms()
        self.refresh_subject_form()
        self.refresh_animation_list()
    def _load_animation_data(self, raw_dir: Path):
        animations = {}
        if raw_dir.is_dir():
            for entry in sorted(raw_dir.iterdir(), key=lambda item: item.name.lower()):
                if entry.is_dir() and not entry.name.startswith((".", "_")):
                    config_path = entry / "config.json"
                    config = self._ensure_animation_defaults(
                        self._read_json(config_path, DEFAULT_ANIMATION_CONFIG)
                    )
                    animations[entry.name] = {"path": config_path, "data": config}
        return animations
    def _ensure_subject_defaults(self, data: dict) -> dict:
        result = copy.deepcopy(data) if isinstance(data, dict) else {}
        result.pop("subject", None)
        result.pop("is_hd", None)
        result.setdefault("resize_to_percent", DEFAULT_SUBJECT_CONFIG["resize_to_percent"])
        result.setdefault("background_color", DEFAULT_SUBJECT_CONFIG["background_color"])
        result.setdefault("color_threshold", DEFAULT_SUBJECT_CONFIG["color_threshold"])
        result.setdefault("remove_background", DEFAULT_SUBJECT_CONFIG.get("remove_background", True))
        result.setdefault("crop_sprites", DEFAULT_SUBJECT_CONFIG.get("crop_sprites", True))
        sheet = result.setdefault("sheet", {})
        sheet.setdefault("width", None)
        sheet.setdefault("height", None)
        return result
    def _sync_reduce_file_size_from_subject(self) -> None:
        if not isinstance(self.subject_config_data, dict):
            return
        previous_value = self.subject_config_data.pop("reduce_file_size", None)
        if previous_value is None:
            return
        if not self._root_config_has_reduce:
            value = bool(previous_value)
            self.reduce_file_size_var.set(value)
            self.root_config["reduce_file_size"] = value
            self._root_config_has_reduce = True
    def _ensure_animation_defaults(self, data: dict) -> dict:
        result = copy.deepcopy(data) if isinstance(data, dict) else {}
        result.setdefault("regenerate", DEFAULT_ANIMATION_CONFIG["regenerate"])
        result.setdefault("delay", DEFAULT_ANIMATION_CONFIG["delay"])
        offset = result.setdefault("offset", {})
        offset.setdefault("x", 0.0)
        offset.setdefault("y", 0.0)
        recover = result.setdefault("recover_cropped_offset", {})
        recover.setdefault("x", True)
        recover.setdefault("y", True)
        return result
    def refresh_subject_form(self) -> None:
        self.resize_var.set(self._format_number(self.subject_config_data.get("resize_to_percent")))
        self._set_background_color_value(self.subject_config_data.get("background_color"))
        self.color_threshold_var.set(self._format_number(self.subject_config_data.get("color_threshold")))
        # boolean fields
        self.remove_background_var.set(bool(self.subject_config_data.get("remove_background", True)))
        self.crop_sprites_var.set(bool(self.subject_config_data.get("crop_sprites", True)))
        sheet = self.subject_config_data.get("sheet", {})
        self.sheet_width_var.set(self._format_number(sheet.get("width")))
        self.sheet_height_var.set(self._format_number(sheet.get("height")))
    def refresh_animation_list(self, preferred=None) -> None:
        self.animation_listbox.delete(0, tk.END)
        self.animation_names = sorted(self.animation_data.keys(), key=lambda name: name.lower())
        for name in self.animation_names:
            self.animation_listbox.insert(tk.END, name)
        if self.animation_names:
            target_name = None
            if preferred in self.animation_names:
                target_name = preferred
            elif self.current_animation in self.animation_names:
                target_name = self.current_animation
            else:
                target_name = self.animation_names[0]
            index = self.animation_names.index(target_name)
            self.animation_listbox.selection_clear(0, tk.END)
            self.animation_listbox.select_set(index)
            self.animation_listbox.activate(index)
            self.animation_listbox.see(index)
            self.display_animation(target_name)
        else:
            self.current_animation = None
            self.clear_animation_form()
            self.update_animation_form_state(False)
    def display_animation(self, name: str) -> None:
        if name not in self.animation_data:
            return
        info = self.animation_data[name]
        data = info["data"]
        self.current_animation = name
        self.anim_rege_var.set(bool(data.get("regenerate", True)))
        self._update_regenerate_dependents_state()
        self.anim_delay_var.set(self._format_number(data.get("delay")))
        offset = data.get("offset", {})
        self.anim_offset_x_var.set(self._format_number(offset.get("x")))
        self.anim_offset_y_var.set(self._format_number(offset.get("y")))
        recover = data.get("recover_cropped_offset", {})
        self.anim_recover_x_var.set(bool(recover.get("x", True)))
        self.anim_recover_y_var.set(bool(recover.get("y", True)))
        self.update_animation_form_state(True)
    def on_animation_selected(self, event=None) -> None:
        selection = self.animation_listbox.curselection()
        if not selection:
            return
        index = selection[0]
        if index >= len(self.animation_names):
            return
        selected_name = self.animation_names[index]
        if self.current_animation == selected_name:
            return
        self._apply_animation_form_to_data()
        self.display_animation(selected_name)
    def ensure_first_animation_selected(self) -> None:
        if not self.animation_names:
            self.clear_animation_form()
            self.update_animation_form_state(False)
            return
        selection = self.animation_listbox.curselection()
        if selection:
            index = selection[0]
            index = min(index, len(self.animation_names) - 1)
        else:
            index = 0
            self.animation_listbox.selection_clear(0, tk.END)
            self.animation_listbox.select_set(index)
            self.animation_listbox.activate(index)
            self.animation_listbox.see(index)
        self.display_animation(self.animation_names[index])
    def reload_animation_directories(self) -> None:
        if not self.current_subject_name:
            return
        self._apply_animation_form_to_data()
        subject_dir = self._resolve_subject_dir(self.current_subject_name, self.current_game_theme)
        raw_dir = subject_dir / "raw"
        previous_selection = self.current_animation
        existing_names = set(self.animation_data.keys())
        discovered_names = []
        if raw_dir.is_dir():
            for entry in sorted(raw_dir.iterdir(), key=lambda item: item.name.lower()):
                if entry.is_dir() and not entry.name.startswith((".", "_")):
                    name = entry.name
                    discovered_names.append(name)
                    if name not in self.animation_data:
                        config_path = entry / "config.json"
                        config = self._ensure_animation_defaults(
                            self._read_json(config_path, DEFAULT_ANIMATION_CONFIG)
                        )
                        self.animation_data[name] = {"path": config_path, "data": config}
        discovered_set = set(discovered_names)
        missing_names = existing_names - discovered_set
        for name in missing_names:
            self.animation_data.pop(name, None)
        self.refresh_animation_list(preferred=previous_selection)
    def toggle_all_animation_regenerate(self) -> None:
        if not self.current_subject_name or not self.animation_data:
            return
        self._apply_animation_form_to_data()
        should_enable = any(
            not bool(info["data"].get("regenerate", True))
            for info in self.animation_data.values()
        )
        new_value = True if should_enable else False
        for info in self.animation_data.values():
            info["data"]["regenerate"] = new_value
        if self.current_animation and self.current_animation in self.animation_data:
            self.anim_rege_var.set(new_value)
    def _apply_subject_form_to_data(self) -> None:
        if not self.subject_config_data:
            return
        self.subject_config_data["resize_to_percent"] = self._parse_number(
            self.resize_var.get(), DEFAULT_SUBJECT_CONFIG["resize_to_percent"]
        )
        color_value = self._finalize_background_color_value()
        self.subject_config_data["background_color"] = color_value
        self.subject_config_data["color_threshold"] = self._parse_number(
            self.color_threshold_var.get(), DEFAULT_SUBJECT_CONFIG["color_threshold"]
        )
        # boolean fields
        self.subject_config_data["remove_background"] = bool(self.remove_background_var.get())
        self.subject_config_data["crop_sprites"] = bool(self.crop_sprites_var.get())
        sheet = self.subject_config_data.setdefault("sheet", {})
        sheet["width"] = self._parse_optional_number(self.sheet_width_var.get())
        sheet["height"] = self._parse_optional_number(self.sheet_height_var.get())
    def _apply_animation_form_to_data(self) -> None:
        if not self.current_animation or self.current_animation not in self.animation_data:
            return
        data = self.animation_data[self.current_animation]["data"]
        data["regenerate"] = bool(self.anim_rege_var.get())
        data["delay"] = self._parse_number(self.anim_delay_var.get(), DEFAULT_ANIMATION_CONFIG["delay"])
        offset = data.setdefault("offset", {})
        offset["x"] = self._parse_number(self.anim_offset_x_var.get(), DEFAULT_ANIMATION_CONFIG["offset"]["x"])
        offset["y"] = self._parse_number(self.anim_offset_y_var.get(), DEFAULT_ANIMATION_CONFIG["offset"]["y"])
        recover = data.setdefault("recover_cropped_offset", {})
        recover["x"] = bool(self.anim_recover_x_var.get())
        recover["y"] = bool(self.anim_recover_y_var.get())
    def _apply_root_form_to_data(self) -> None:
        subject_value = self.subject_var.get().strip()
        subject_value = subject_value if subject_value else None
        game_theme_value = self._get_selected_game_theme()
        self.current_game_theme = game_theme_value
        hd_value = self._normalize_is_hd_value(self.is_hd_theme_var.get())
        if game_theme_value:
            self._update_theme_subject(game_theme_value, subject_value)
            self._update_theme_is_hd(game_theme_value, hd_value)
        else:
            self._set_none_is_hd(hd_value)
            self._set_none_subject(subject_value)
        self._sync_root_subject_field()
        self.root_config["game_theme"] = game_theme_value
        self.root_config["reduce_file_size"] = bool(self.reduce_file_size_var.get())
        self._root_config_has_reduce = True
    
    def _snapshot_current_subject(self) -> None:
        if not self.current_subject_name or not self.subject_config_path:
            return
        self._apply_subject_form_to_data()
        self._apply_animation_form_to_data()
        stored = {
            "config_path": self.subject_config_path,
            "config_data": copy.deepcopy(self.subject_config_data),
            "animations": {
                name: {"path": info["path"], "data": copy.deepcopy(info["data"])}
                for name, info in self.animation_data.items()
            },
        }
        key = self._subject_store_key(self.current_game_theme, self.current_subject_name)
        self.subject_store[key] = stored

    def save_all(self, show_message: bool = True) -> bool:
        self.refresh_animation_list()
        self._snapshot_current_subject()
        self._apply_root_form_to_data()
        try:
            self._write_json(self.root_config_path, self.root_config)
            active_theme = self._get_selected_game_theme()
            if active_theme:
                payload = self._build_theme_config_payload(active_theme)
                target_path = self._resolve_theme_config_path(active_theme)
                self._write_json(target_path, payload)
            for (theme_key, _), payload in list(self.subject_store.items()):
                if theme_key != active_theme:
                    continue
                self._write_json(payload["config_path"], payload["config_data"])
                for info in payload["animations"].values():
                    self._write_json(info["path"], info["data"])
        except OSError as exc:
            messagebox.showerror("Save Failed", f"Could not save configuration files\n{exc}")
            return False
        if show_message:
            messagebox.showinfo("Saved", "Configuration files have been saved.")
        if active_theme:
            self.theme_config_cache = {active_theme: self.theme_config_cache.get(active_theme, {})}
        else:
            self.theme_config_cache = {}
        self.subject_store = {key: value for key, value in self.subject_store.items() if key[0] == active_theme}
        return True
    
    def save_and_generate(self) -> None:
        if not self.save_all(show_message=False):
            return
        if getattr(sys, "frozen", False):
            success = self._run_generator_embedded()
        else:
            success = self._run_generator_subprocess()
        if success:
            messagebox.showinfo(
                'Save & Generate',
                "Saved configuration and generated the spritesheet into <SubjectName>/generated successfully.",
            )
    def _run_generator_subprocess(self) -> bool:
        try:
            result = subprocess.run(
                [sys.executable, str(self.root_dir / "sprite_rips_to_mm_sprite_resources.py")],
                cwd=self.root_dir,
                capture_output=True,
                text=True,
            )
        except OSError as exc:
            messagebox.showerror('Save & Generate', f"Failed to run sprite_rips_to_mm_sprite_resources.py:\n{exc}")
            return False
        if result.returncode == 0:
            return True
        error_output = result.stderr.strip() or result.stdout.strip() or "No output."
        messagebox.showerror(
            'Save & Generate',
            f"sprite_rips_to_mm_sprite_resources.py exited with code {result.returncode}:\n\n{error_output}",
        )
        return False
    def _run_generator_embedded(self) -> bool:
        try:
            from sprite_rips_to_mm_sprite_resources import main as generator_main
        except Exception as exc:
            messagebox.showerror(
                'Save & Generate',
                "Failed to import sprite_rips_to_mm_sprite_resources:\n" + str(exc),
            )
            return False
        previous_cwd = os.getcwd()
        try:
            os.chdir(self.root_dir)
            try:
                generator_main()
            except SystemExit as exc:
                code = exc.code
                if code not in (None, 0):
                    if isinstance(code, int):
                        message = f"sprite_rips_to_mm_sprite_resources exited with code {code}."
                    else:
                        message = str(code) or "sprite_rips_to_mm_sprite_resources exited with a non-zero status."
                    messagebox.showerror('Save & Generate', message)
                    return False
        except Exception as exc:
            messagebox.showerror(
                'Save & Generate',
                "Failed to run embedded generator:\n" + str(exc),
            )
            return False
        finally:
            os.chdir(previous_cwd)
        return True
    def _write_json(self, path: Path, data: dict) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=2)
            handle.write("\n")
    def _read_json(self, path: Path, default=None) -> dict:
        if path.exists():
            try:
                with path.open("r", encoding="utf-8") as handle:
                    loaded = json.load(handle)
                if isinstance(loaded, dict):
                    return loaded
            except json.JSONDecodeError as exc:
                messagebox.showerror("JSON Error", f"Failed to parse {path.name}:\n{exc}")
        return copy.deepcopy(default) if default is not None else {}
    def _normalize_background_color_value(self, value: object) -> str:
        text = str(value).strip() if value is not None else ''
        if not text:
            return ''
        if text.startswith('#'):
            text = text[1:]
        text = text.upper()
        if len(text) in (3, 4):
            text = ''.join(ch * 2 for ch in text)
        if len(text) not in (6, 8):
            raise ValueError
        valid = set('0123456789ABCDEF')
        if any(ch not in valid for ch in text):
            raise ValueError
        return '#' + text

    def _set_background_color_value(self, value: object) -> None:
        try:
            normalized = self._normalize_background_color_value(value) if value else ''
        except ValueError:
            normalized = ''
        self._last_valid_background_color = normalized
        if hasattr(self, 'color_remove_var') and isinstance(self.color_remove_var, tk.StringVar):
            self._is_setting_background_color = True
            try:
                self.color_remove_var.set(normalized)
            finally:
                self._is_setting_background_color = False
        self._update_background_color_preview(normalized or None, True)

    def _update_background_color_preview(self, color_value, is_valid: bool = True) -> None:
        if getattr(self, 'color_preview', None) is None or getattr(self, '_color_preview_rect', None) is None:
            return
        fill = self._default_color_preview_fill or self.color_preview.cget('background')
        if color_value:
            fill = str(color_value)[:7]
        self.color_preview.itemconfig(self._color_preview_rect, fill=fill)
        border = "#b3b3b300" if is_valid else '#cc4c4c'
        self.color_preview.configure(highlightbackground=border)

    def _on_background_color_change(self, *_: object) -> None:
        if self._is_setting_background_color:
            return
        value = self.color_remove_var.get().strip() if hasattr(self, 'color_remove_var') else ''
        if not value:
            self._update_background_color_preview(None, True)
            return
        try:
            normalized = self._normalize_background_color_value(value)
        except ValueError:
            self._update_background_color_preview(None, False)
        else:
            self._update_background_color_preview(normalized, True)

    def _on_background_color_focus_out(self, event: object) -> None:
        value = self.color_remove_var.get() if hasattr(self, 'color_remove_var') else ''
        text = value.strip() if isinstance(value, str) else ''
        if not text:
            self._set_background_color_value('')
            return
        try:
            normalized = self._normalize_background_color_value(text)
        except ValueError:
            normalized = self._last_valid_background_color or ''
        self._set_background_color_value(normalized)

    def _finalize_background_color_value(self) -> str:
        value = self.color_remove_var.get() if hasattr(self, 'color_remove_var') else ''
        text = value.strip() if isinstance(value, str) else ''
        if not text:
            self._set_background_color_value('')
            return ''
        try:
            normalized = self._normalize_background_color_value(text)
        except ValueError:
            normalized = self._last_valid_background_color or ''
        self._set_background_color_value(normalized)
        return self._last_valid_background_color

    def _validate_integer_input(self, proposed: str) -> bool:
        if proposed is None:
            return False
        if proposed == '':
            return True
        return proposed.isdigit()

    def _validate_signed_integer_input(self, proposed: str) -> bool:
        if proposed is None:
            return False
        if proposed in ('', '-'):
            return True
        if proposed.startswith('-'):
            return proposed[1:].isdigit()
        return proposed.isdigit()

    def _parse_number(self, value: object, default: object) -> object:
        text = str(value).strip() if value is not None else ""
        if not text:
            return default
        try:
            numeric = float(text)
        except ValueError:
            return default
        if numeric.is_integer():
            return int(numeric)
        return numeric
    def _parse_optional_number(self, value: object):
        text = str(value).strip() if value is not None else ""
        if not text:
            return None
        parsed = self._parse_number(text, None)
        return parsed
    def _format_number(self, value: object) -> str:
        if value is None:
            return ""
        if isinstance(value, (int, float)):
            if isinstance(value, float) and value.is_integer():
                return str(int(value))
            return str(value)
        return str(value)
    def on_game_theme_change(self, event=None) -> None:
        new_theme = self._get_selected_game_theme()
        previous_theme = self.current_game_theme
        if new_theme == previous_theme:
            return
        self._snapshot_current_subject()
        self.current_game_theme = new_theme
        self.root_config["game_theme"] = new_theme
        self._sync_root_subject_field()
        self.is_hd_theme_var.set(self._resolve_is_hd_for_theme(new_theme))
        if previous_theme is None and new_theme is not None:
            display_name = new_theme
            messagebox.showinfo("Game Theme Selected", f"Game theme '{display_name}' is now active. Subjects will be loaded from that folder.")
        elif previous_theme is not None and new_theme is None:
            messagebox.showinfo("Game Theme Cleared", "Game theme cleared. Subjects will be loaded from the root directory.")
        self.populate_subject_options()
        subjects = list(self.subject_options)
        if not subjects:
            self.subject_var.set("")
            self.subject_combo.set("")
            self.disable_subject_forms()
            self.current_subject_name = None
            if new_theme:
                self._update_theme_subject(new_theme, None)
            else:
                self._set_none_subject(None)
            return
        if new_theme:
            stored_subject = self._get_theme_selected_subject(new_theme)
        else:
            stored_subject = self.root_config.get("subject")
        if stored_subject in subjects:
            selected_subject = stored_subject
        else:
            selected_subject = subjects[0]
        self.subject_var.set(selected_subject)
        self.subject_combo.set(selected_subject)
        if new_theme:
            self._update_theme_subject(new_theme, selected_subject)
        else:
            self._set_none_subject(selected_subject)
        self.load_subject(selected_subject)
    def on_hd_theme_toggle(self) -> None:
        value = self._normalize_is_hd_value(self.is_hd_theme_var.get())
        active_theme = self._get_selected_game_theme()
        if active_theme:
            self._update_theme_is_hd(active_theme, value)
        else:
            self._set_none_is_hd(value)
        self._sync_root_subject_field()

    def on_subject_change(self, event=None) -> None:
        new_subject = self.subject_var.get()
        if not new_subject or new_subject == self.current_subject_name:
            return
        self._snapshot_current_subject()
        game_theme = self._get_selected_game_theme()
        if game_theme:
            self._update_theme_subject(game_theme, new_subject)
        else:
            self._set_none_subject(new_subject)
        self.root_config["game_theme"] = game_theme
        self.load_subject(new_subject)
    
    def on_notebook_tab_changed(self, event: object) -> None:
        tab_id = event.widget.select()
        if tab_id == str(self.animations_tab):
            self.ensure_first_animation_selected()
    def show_about(self) -> None:
        msg = (
            "<Name>\n"
            "├─ sprite_rips_to_mm_sprite_resources.exe\n"
            "├─ <SubjectName>\n"
            "│  └─ raw\n"
            "│     ├─ <AnimationName>\n"
            "│     │  ├─ frame001.png\n"
            "│     │  └─ frame002.png\n"
            "│     ├─ <AnimationName>\n"
            "│     └─ <AnimationName>\n"
            "├─ <SubjectName>\n"  
            "└─ <SubjectName>"            
        )
        win = tk.Toplevel(self)
       
        win.resizable(False, False)
        win.title("About / Help")
        win.transient(self)
        if getattr(self, "icon_path", None):
            try:
                win.iconbitmap(self.icon_path)
            except Exception:
                pass
        win.grab_set()
  
        try:
            mono = tkfont.Font(family="Courier New", size=10)
        except Exception:
            mono = tkfont.nametofont("TkFixedFont")
        frame = ttk.Frame(win, padding=10)
        frame.pack(fill="both", expand=True)
        title_label = ttk.Label(frame, text="Frames to MM sprite resources v1.2", font=("TkDefaultFont", 12, "bold"))
        title_label.grid(row=0, column=0, sticky="w", pady=(0, 8))
        header_label = ttk.Label(frame, text="Created by Marci599 for Mario Multiverse (created by neoarc).")
        header_label.grid(row=1, column=0, sticky="w", pady=(0, 8))
        lin_header_label = ttk.Label(frame, text="Check GitHub repository for help, updates and issues:")
        lin_header_label.grid(row=2, column=0, sticky="w", pady=(0, 0))
        link_url = "https://github.com/Marci599/sprite-rips-to-mm-sprite-resources" 
        link_label = tk.Label(frame, text="github.com/Marci599/sprite-rips-to-mm-sprite-resources", fg="blue", cursor="hand2")
        link_label.grid(row=3, column=0, sticky="w", pady=(0, 8))
        link_label.bind("<Button-1>", lambda e: webbrowser.open_new(link_url))
    def reload_subjects(self) -> None:
        self._snapshot_current_subject()
        current_theme_value = self.game_theme_var.get()
        self.populate_game_theme_options()
        if current_theme_value not in self.game_theme_options:
            current_theme_value = self.game_theme_options[0] if self.game_theme_options else self._format_game_theme_value(None)
            self.game_theme_var.set(current_theme_value)
            self.game_theme_combo.set(current_theme_value)
        new_game_theme = self._parse_game_theme_value(current_theme_value)
        self.current_game_theme = new_game_theme
        self.root_config["game_theme"] = new_game_theme
        self._sync_root_subject_field()
        current_subject = self.subject_var.get()
        self.populate_subject_options()
        subjects = list(self.subject_options)
        if not subjects:
            self.subject_var.set("")
            self.subject_combo.set("")
            self.disable_subject_forms()
            self.current_subject_name = None
            if new_game_theme:
                self._update_theme_subject(new_game_theme, None)
            else:
                self._set_none_subject(None)
            return
        preferred_subject = None
        if new_game_theme:
            stored_subject = self._get_theme_selected_subject(new_game_theme)
            if stored_subject in subjects:
                preferred_subject = stored_subject
        if not preferred_subject and current_subject in subjects:
            preferred_subject = current_subject
        if not preferred_subject and not new_game_theme:
            stored_root = self.root_config.get("subject")
            if stored_root in subjects:
                preferred_subject = stored_root
        if not preferred_subject:
            preferred_subject = subjects[0]
        self.subject_var.set(preferred_subject)
        self.subject_combo.set(preferred_subject)
        if new_game_theme:
            self._update_theme_subject(new_game_theme, preferred_subject)
        else:
            self._set_none_subject(preferred_subject)
        self.load_subject(preferred_subject)
        self.reload_animation_directories()
    
    def clear_subject_form(self) -> None:
        self.resize_var.set("")
        self._set_background_color_value("")
        self.color_threshold_var.set("")
        self.sheet_width_var.set("")
        self.sheet_height_var.set("")
        self.remove_background_var.set(DEFAULT_SUBJECT_CONFIG.get("remove_background", True))
        self.crop_sprites_var.set(DEFAULT_SUBJECT_CONFIG.get("crop_sprites", True))
    def clear_animation_form(self) -> None:
        self.anim_rege_var.set(True)
        self._update_regenerate_dependents_state()
        self.anim_delay_var.set("")
        self.anim_offset_x_var.set("")
        self.anim_offset_y_var.set("")
        self.anim_recover_x_var.set(True)
        self.anim_recover_y_var.set(True)
    def disable_subject_forms(self) -> None:
        if hasattr(self, "is_hd_check"):
            self.is_hd_check.state(["disabled"])
        for entry in self.subject_entries:
            entry.state(["disabled"])
        self.animation_listbox.delete(0, tk.END)
        self.animation_names = []
        self.current_animation = None
        self.animation_data = {}
    def enable_subject_forms(self) -> None:
        if hasattr(self, "is_hd_check"):
            self.is_hd_check.state(["!disabled"])
        for entry in self.subject_entries:
            entry.state(["!disabled"])
    def update_animation_form_state(self, enabled: bool) -> None:
        self._animation_form_enabled = enabled
        state = ["!disabled"] if enabled else ["disabled"]
        for widget in self.animation_form_widgets:
            widget.state(state)
        if enabled:
            self._update_regenerate_dependents_state()

    def _on_animation_regenerate_change(self, *_: object) -> None:
        self._update_regenerate_dependents_state()

    def _update_regenerate_dependents_state(self) -> None:
        if not getattr(self, '_animation_form_enabled', False):
            return
        dependents = [
            getattr(self, 'offset_group', None),
            getattr(self, 'offset_x_entry', None),
            getattr(self, 'offset_y_entry', None),
            getattr(self, 'recover_group', None),
            getattr(self, 'recover_x_check', None),
            getattr(self, 'recover_y_check', None),
        ]
        enabled = bool(self.anim_rege_var.get())
        state = ["!disabled"] if enabled else ["disabled"]
        for widget in dependents:
            if widget is not None:
                widget.state(state)
def main() -> None:
    app = ConfigManagerUI()
    app.mainloop()
if __name__ == "__main__":
    main()
