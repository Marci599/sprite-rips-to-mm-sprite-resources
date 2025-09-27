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
    "sheet": {"width": None, "height": None},
}

DEFAULT_ANIMATION_CONFIG = {
    "regenerate": True,
    "delay": 1,
    "offset": {"x": 0.0, "y": 0.0},
    "recover_cropped_offset": {"x": True, "y": True},
}




ASSET_BUNDLE_DIR = "assets"


def resolve_storage_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def resolve_asset_source() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys._MEIPASS) / ASSET_BUNDLE_DIR
    return Path(__file__).resolve().parent



class ConfigManagerUI(tk.Tk):
    def __init__(self) -> None:
        super().__init__()

        self.title("Sprite rips to MM sprite resources")
        
        self.minsize(705, 505)

        self.root_dir = resolve_storage_root()
        self.bundle_assets_dir = resolve_asset_source()
        self._ensure_runtime_assets()

        icon_path = self.root_dir / "icon.ico"
        if icon_path.exists():
            try:
                self.icon_path = str(icon_path)
                self.iconbitmap(self.icon_path)
            except Exception:
                self.icon_path = None

        self.root_config_path = self.root_dir / "config.json"
        self.root_config = self._read_json(self.root_config_path, {"subject": None})
        if not isinstance(self.root_config, dict):
            self.root_config = {"subject": None}
        self.root_config.setdefault("subject", None)

        self.subject_var = tk.StringVar()

        self.subject_config_path = None
        self.subject_config_data = {}
        self.animation_data = {}
        self.animation_names = []
        self.current_subject_name = None
        self.current_animation = None
        self.subject_store = {}
        self.subject_options = []

        self._build_ui()
        self.populate_subject_options()
        self._initialize_selection()

        self.notebook.bind("<<NotebookTabChanged>>", self.on_notebook_tab_changed)

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

        subject_header = ttk.Frame(self)
        subject_header.pack(fill="x", padx=outer_padding, pady=(outer_padding, 0))
        subject_header.columnconfigure(1, weight=1)

        ttk.Label(subject_header, text="Subject:").grid(row=0, column=0, sticky="w")
        self.subject_combo = ttk.Combobox(subject_header, textvariable=self.subject_var, state="readonly")
        self.subject_combo.grid(row=0, column=1, sticky="ew", padx=(6, 0))
        self.subject_combo.bind("<<ComboboxSelected>>", self.on_subject_change)
        self.reload_button = ttk.Button(subject_header, text="Refresh", command=self.reload_subjects)
        self.reload_button.grid(row=0, column=2, sticky="w", padx=(8, 0))

        self.help_button = ttk.Button(subject_header, text="?", width=3, command=self.show_about)
        self.help_button.grid(row=0, column=3, sticky="e", padx=(8, 0))


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

        self.resize_var = tk.StringVar()
        ttk.Label(processing_group, text="Resize to percent").grid(row=0, column=0, sticky="w", pady=4)
        resize_entry = ttk.Entry(processing_group, textvariable=self.resize_var)
        resize_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0), pady=4)
        self.subject_entries.append(resize_entry)

        self.color_remove_var = tk.StringVar()
        ttk.Label(processing_group, text="Background color").grid(row=1, column=0, sticky="w", pady=4)
        color_entry = ttk.Entry(processing_group, textvariable=self.color_remove_var)
        color_entry.grid(row=1, column=1, sticky="ew", padx=(8, 0), pady=4)
        self.subject_entries.append(color_entry)

        self.color_threshold_var = tk.StringVar()
        ttk.Label(processing_group, text="Color threshold").grid(row=2, column=0, sticky="w", pady=4)
        threshold_entry = ttk.Entry(processing_group, textvariable=self.color_threshold_var)
        threshold_entry.grid(row=2, column=1, sticky="ew", padx=(8, 0), pady=4)
        self.subject_entries.append(threshold_entry)

        # New boolean options: remove background and crop sprites
        self.remove_background_var = tk.BooleanVar(value=True)
        remove_bg_check = ttk.Checkbutton(
            processing_group,
            text="Remove background",
            variable=self.remove_background_var,
        )
        remove_bg_check.grid(row=3, column=0, columnspan=2, sticky="w", pady=6)
        self.subject_entries.append(remove_bg_check)

        self.crop_sprites_var = tk.BooleanVar(value=True)
        crop_check = ttk.Checkbutton(
            processing_group,
            text="Crop sprites",
            variable=self.crop_sprites_var,
        )
        crop_check.grid(row=4, column=0, columnspan=2, sticky="w", pady=(0,6))
        self.subject_entries.append(crop_check)

        ttk.Label(processing_group, text="Cropping reduces file size", foreground="gray",).grid(row=5, column=0, columnspan=2, sticky="w", pady=(0, 4))

        sheet_group = ttk.LabelFrame(subject_groups, text="Sheet dimensions", padding=section_padding)
        sheet_group.grid(row=0, column=1, sticky="nsew", padx=(column_gap, 0))
        sheet_group.columnconfigure(1, weight=1)

        self.sheet_width_var = tk.StringVar()
        ttk.Label(sheet_group, text="Width").grid(row=0, column=0, sticky="w", pady=4)
        sheet_width_entry = ttk.Entry(sheet_group, textvariable=self.sheet_width_var)
        sheet_width_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0), pady=4)
        self.subject_entries.append(sheet_width_entry)

        self.sheet_height_var = tk.StringVar()
        ttk.Label(sheet_group, text="Height").grid(row=1, column=0, sticky="w", pady=4)
        sheet_height_entry = ttk.Entry(sheet_group, textvariable=self.sheet_height_var)
        sheet_height_entry.grid(row=1, column=1, sticky="ew", padx=(8, 0), pady=4)
        self.subject_entries.append(sheet_height_entry)

        ttk.Label(sheet_group, text="Leave blank for automatic sizing", foreground="gray",).grid(
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
        regen_check = ttk.Checkbutton(detail_frame, text="Regenerate", variable=self.anim_rege_var)
        regen_check.grid(row=0, column=0, columnspan=2, sticky="w", pady=4)
        self.animation_form_widgets.append(regen_check)

        ttk.Label(detail_frame, text="If disabled, already generated sprites will be added to the spritesheet.\nEdited offsets will remain unchanged.", foreground="gray",).grid(
            row=1, column=0, columnspan=2, sticky="w", pady=(4, 0)
        )
        self.animation_form_widgets.append(regen_check)

        self.anim_delay_var = tk.StringVar()
        ttk.Label(detail_frame, text="Delay").grid(row=2, column=0, sticky="w", pady=4)
        delay_entry = ttk.Entry(detail_frame, textvariable=self.anim_delay_var)
        delay_entry.grid(row=2, column=1, sticky="ew", padx=(8, 0), pady=4)
        self.animation_form_widgets.append(delay_entry)

        offset_group = ttk.LabelFrame(detail_frame, text="Offset", padding=section_padding)
        offset_group.grid(row=3, column=0, columnspan=2, sticky="ew", pady=(12, 0))
        offset_group.columnconfigure(1, weight=1)
        offset_group.columnconfigure(3, weight=1)

        self.anim_offset_x_var = tk.StringVar()
        ttk.Label(offset_group, text="X").grid(row=0, column=0, sticky="w", pady=4)
        offset_x_entry = ttk.Entry(offset_group, textvariable=self.anim_offset_x_var)
        offset_x_entry.grid(row=0, column=1, sticky="ew", padx=(8, column_gap), pady=4)
        self.animation_form_widgets.append(offset_x_entry)

        self.anim_offset_y_var = tk.StringVar()
        ttk.Label(offset_group, text="Y").grid(row=0, column=2, sticky="w", pady=4)
        offset_y_entry = ttk.Entry(offset_group, textvariable=self.anim_offset_y_var)
        offset_y_entry.grid(row=0, column=3, sticky="ew", padx=(8, 0), pady=4)
        self.animation_form_widgets.append(offset_y_entry)

        ttk.Label(
            offset_group,
            text="Adjust the offset for every sprite within the selected animation",

            foreground="gray",
        ).grid(row=1, column=0, columnspan=4, sticky="w", pady=(4, 0))
        self.animation_form_widgets.append(offset_group)


        recover_group = ttk.LabelFrame(detail_frame, text="Recover cropped offset", padding=section_padding)
        recover_group.grid(row=4, column=0, columnspan=2, sticky="ew", pady=(12, 0))
        recover_group.columnconfigure(0, weight=1)
        recover_group.columnconfigure(1, weight=1)

        self.anim_recover_x_var = tk.BooleanVar(value=True)
        recover_x_check = ttk.Checkbutton(
            recover_group,
            text="X",
            variable=self.anim_recover_x_var,
        )
        recover_x_check.grid(row=0, column=0, sticky="w", pady=4, padx=(0, column_gap))
        self.animation_form_widgets.append(recover_x_check)

        self.anim_recover_y_var = tk.BooleanVar(value=True)
        recover_y_check = ttk.Checkbutton(
            recover_group,
            text="Y",
            variable=self.anim_recover_y_var,
        )
        recover_y_check.grid(row=0, column=1, sticky="w", pady=4, padx=(column_gap, 0))
        self.animation_form_widgets.append(recover_y_check)

        ttk.Label(
            recover_group,
            text="If enabled, the offsets will be adjusted to account for any cropping that was done.\n" \
            "If your subject moves around, disable the axis it moves on, and adjust them later.",
            foreground="gray",
        ).grid(row=1, column=0, columnspan=2, sticky="w", pady=(4, 0))
        self.animation_form_widgets.append(recover_group)

        detail_frame.rowconfigure(5, weight=1)

    

        bottom_frame = ttk.Frame(self, padding=(outer_padding, 0, outer_padding, outer_padding))
        bottom_frame.pack(fill="x")

        self.save_and_generate_button = ttk.Button(
            bottom_frame, text="Save & Generate", command=self.save_and_generate
        )
        self.save_and_generate_button.pack(side="right")
        self.save_button = ttk.Button(bottom_frame, text="Save All", command=self.save_all)
        self.save_button.pack(side="right", padx=(0, 8))

        self.clear_subject_form()
        self.clear_animation_form()
        self.update_animation_form_state(False)

    def populate_subject_options(self) -> None:
        subjects = self.discover_subjects()
        self.subject_options = subjects
        self.subject_combo["values"] = subjects

    def _initialize_selection(self) -> None:
        subjects = list(self.subject_combo["values"])
        if not subjects:
            self.subject_var.set("")
            self.disable_subject_forms()
            return

        preferred = self.root_config.get("subject")
        if preferred in subjects:
            selected = preferred
        else:
            selected = subjects[0]

        self.subject_var.set(selected)
        self.subject_combo.set(selected)
        self.root_config["subject"] = selected

        self.load_subject(selected)

    def discover_subjects(self):
        subjects = []
        for entry in sorted(self.root_dir.iterdir(), key=lambda item: item.name.lower()):
            if entry.is_dir() and not entry.name.startswith((".", "_")):
                subjects.append(entry.name)
        return subjects

    def load_subject(self, subject: str) -> None:
        subject_dir = self.root_dir / subject
        config_path = subject_dir / "config.json"

        if subject in self.subject_store:
            stored = self.subject_store[subject]
            self.subject_config_data = copy.deepcopy(stored.get("config_data", {}))
            animations = {}
            for name, info in stored.get("animations", {}).items():
                animations[name] = {"path": info["path"], "data": copy.deepcopy(info["data"])}
            self.animation_data = animations
        else:
            subject_data = self._ensure_subject_defaults(self._read_json(config_path, DEFAULT_SUBJECT_CONFIG))
            self.subject_config_data = subject_data
            self.animation_data = self._load_animation_data(subject_dir / "raw")

        self.subject_config_path = config_path
        self.current_subject_name = subject
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
        result.setdefault("resize_to_percent", DEFAULT_SUBJECT_CONFIG["resize_to_percent"])
        result.setdefault("background_color", DEFAULT_SUBJECT_CONFIG["background_color"])
        result.setdefault("color_threshold", DEFAULT_SUBJECT_CONFIG["color_threshold"])
        result.setdefault("remove_background", DEFAULT_SUBJECT_CONFIG.get("remove_background", True))
        result.setdefault("crop_sprites", DEFAULT_SUBJECT_CONFIG.get("crop_sprites", True))
        sheet = result.setdefault("sheet", {})
        sheet.setdefault("width", None)
        sheet.setdefault("height", None)
        return result

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
        self.color_remove_var.set(str(self.subject_config_data.get("background_color", "")))
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
        raw_dir = self.root_dir / self.current_subject_name / "raw"
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
        self.subject_config_data["background_color"] = self.color_remove_var.get().strip()
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
        self.root_config["subject"] = subject_value if subject_value else None

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
        self.subject_store[self.current_subject_name] = stored

    def save_all(self, show_message: bool = True) -> bool:
        self._snapshot_current_subject()
        self._apply_root_form_to_data()
        try:
            self._write_json(self.root_config_path, self.root_config)
            for subject, payload in self.subject_store.items():
                self._write_json(payload["config_path"], payload["config_data"])
                for info in payload["animations"].values():
                    self._write_json(info["path"], info["data"])
        except OSError as exc:
            messagebox.showerror("Save Failed", f"Could not save configuration files:\n{exc}")
            return False
        if show_message:
            messagebox.showinfo("Saved", "Configuration files have been saved.")
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
                "Save & Generate",
                "Saved configuration and regenerated the spritesheet successfully.",
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
            messagebox.showerror("Save & Generate", f"Failed to run sprite_rips_to_mm_sprite_resources.py:\n{exc}")
            return False

        if result.returncode == 0:
            return True

        error_output = result.stderr.strip() or result.stdout.strip() or "No output."
        messagebox.showerror(
            "Save & Generate",
            f"sprite_rips_to_mm_sprite_resources.py exited with code {result.returncode}:\n\n{error_output}",
        )
        return False

    def _run_generator_embedded(self) -> bool:
        try:
            from sprite_rips_to_mm_sprite_resources import main as generator_main
        except Exception as exc:
            messagebox.showerror(
                "Save & Generate",
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
                    messagebox.showerror("Save & Generate", message)
                    return False
        except Exception as exc:
            messagebox.showerror(
                "Save & Generate",
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

    def on_subject_change(self, event=None) -> None:
        new_subject = self.subject_var.get()
        if not new_subject or new_subject == self.current_subject_name:
            return
        self._snapshot_current_subject()
        self.root_config["subject"] = new_subject
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
            "└─ <SubjectName>\n\n"            
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
        title_label = ttk.Label(frame, text="Sprite rips to MM sprite resources v1.0", font=("TkDefaultFont", 12, "bold"))
        title_label.grid(row=0, column=0, sticky="w", pady=(0, 8))
        header_label = ttk.Label(frame, text="Created by Marci599 for Mario Multiverse (created by neoarc).")
        header_label.grid(row=1, column=0, sticky="w", pady=(0, 8))
        lin_header_label = ttk.Label(frame, text="Check GitHub repository for updates and issues:")
        lin_header_label.grid(row=2, column=0, sticky="w", pady=(0, 0))
        link_url = "https://github.com/Marci599/sprite-rips-to-mm-sprite-resources" 
        link_label = tk.Label(frame, text=link_url, fg="blue", cursor="hand2")
        link_label.grid(row=3, column=0, sticky="w", pady=(0, 8))

        def _underline_enter(event):
            try:
                f = tkfont.Font(link_label, link_label.cget("font"))
                f.configure(underline=True)
                link_label.configure(font=f)
            except Exception:
                pass

        def _underline_leave(event):
            try:
                f = tkfont.Font(link_label, link_label.cget("font"))
                f.configure(underline=False)
                link_label.configure(font=f)
            except Exception:
                pass

        link_label.bind("<Enter>", _underline_enter)
        link_label.bind("<Leave>", _underline_leave)
        link_label.bind("<Button-1>", lambda e: webbrowser.open_new(link_url))

        structure_label = ttk.Label(frame, text="Structure your files as follows:")
        structure_label.grid(row=4, column=0, sticky="w", pady=(0, 4))


        text_widget = tk.Text(frame, wrap="none", font=mono, height=12, width=15)
        text_widget.insert("1.0", msg)
        text_widget.config(state="disabled")
        text_widget.grid(row=5, column=0, sticky="nsew")
        yscroll = ttk.Scrollbar(frame, orient="vertical", command=text_widget.yview)
        yscroll.grid(row=5, column=1, sticky="ns")
        text_widget.configure(yscrollcommand=yscroll.set)
        xscroll = ttk.Scrollbar(frame, orient="horizontal", command=text_widget.xview)
        xscroll.grid(row=6, column=0, sticky="ew")
        text_widget.configure(xscrollcommand=xscroll.set)
        frame.columnconfigure(0, weight=1)
        frame.rowconfigure(0, weight=1)
        footer_label = ttk.Label(frame, text="After placing your subjects, launch the program, select a subject,\nconfigure options, and use 'Save & Generate' to create the\nspritesheetresources into <SubjectName>/generated.")
        footer_label.grid(row=7, column=0, sticky="w", pady=(8, 0))

        close_btn = ttk.Button(frame, text="Close", command=win.destroy)
        close_btn.grid(row=8, column=0, pady=(8, 0))

    def reload_subjects(self) -> None:
        current_subject = self.subject_var.get()
        self.populate_subject_options()
        subjects = list(self.subject_options)
        if not subjects:
            self.subject_var.set("")
            self.disable_subject_forms()
            self.current_subject_name = None
            self.root_config["subject"] = None
            return

        if current_subject in subjects:
            self.subject_combo.set(current_subject)
        else:
            new_subject = subjects[0]
            self.subject_var.set(new_subject)
            self.subject_combo.set(new_subject)
            self._snapshot_current_subject()
            self.load_subject(new_subject)
            self.root_config["subject"] = new_subject

    def clear_subject_form(self) -> None:
        self.resize_var.set("")
        self.color_remove_var.set("")
        self.color_threshold_var.set("")
        self.sheet_width_var.set("")
        self.sheet_height_var.set("")
        # reset new boolean fields to defaults
        self.remove_background_var.set(DEFAULT_SUBJECT_CONFIG.get("remove_background", True))
        self.crop_sprites_var.set(DEFAULT_SUBJECT_CONFIG.get("crop_sprites", True))

    def clear_animation_form(self) -> None:
        self.anim_rege_var.set(True)
        self.anim_delay_var.set("")
        self.anim_offset_x_var.set("")
        self.anim_offset_y_var.set("")
        self.anim_recover_x_var.set(True)
        self.anim_recover_y_var.set(True)

    def disable_subject_forms(self) -> None:
        for entry in self.subject_entries:
            entry.state(["disabled"])
        self.animation_listbox.delete(0, tk.END)
        self.animation_names = []
        self.current_animation = None
        self.animation_data = {}
        self.clear_animation_form()
        self.update_animation_form_state(False)

    def enable_subject_forms(self) -> None:
        for entry in self.subject_entries:
            entry.state(["!disabled"])

    def update_animation_form_state(self, enabled: bool) -> None:
        state = ["!disabled"] if enabled else ["disabled"]
        for widget in self.animation_form_widgets:
            widget.state(state)


def main() -> None:
    app = ConfigManagerUI()
    app.mainloop()


if __name__ == "__main__":
    main()








