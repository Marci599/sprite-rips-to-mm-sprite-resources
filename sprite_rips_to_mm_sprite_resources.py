import json
import math
import pathlib
import shutil
from collections.abc import Sequence
from typing import Any, Dict, List, Optional, Tuple
from dataclasses import dataclass
import numpy as np

try:
    from PIL import Image
except ImportError as exc:
    raise SystemExit("Pillow is required to run this script. Install it with `pip install pillow`.") from exc

try:
    RESAMPLE_NEAREST = Image.Resampling.NEAREST
except AttributeError:
    RESAMPLE_NEAREST = Image.NEAREST

SUPPORTED_EXTENSIONS = {".png"}
LAYOUT_GAP = 2
CONFIG_PATH = "config.json"

DEFAULT_SUBJECT_CONFIG: Dict[str, Any] = {
    "resize_to_percent": 100,
    "background_color": "#00FF00",
    "color_threshold": 100,
    "remove_background": True,
    "crop_sprites": True,
    "reduce_file_size": False,
    "sheet": {
        "width": None,
        "height": None
    }
}

DEFAULT_ANIMATION_CONFIG: Dict[str, Any] = {
    "regenerate": True,
    "delay": 1,
    "offset": {
        "x": 0,
        "y": 0
    },
    "recover_cropped_offset": {
        "x": True,
        "y": True
    }
}

@dataclass
class SubjectConfig:
    resize_to_percent: float
    background_color: Optional[Tuple[int, int, int, int]]
    color_threshold: float
    remove_background: bool
    crop_sprites: bool
    sheet_dimensions: Tuple[Optional[int], Optional[int]]

@dataclass
class AnimationConfig:
    regenerate: bool
    delay: int
    offset: Tuple[float, float]
    recover_cropped_offset: Tuple[bool, bool]

#@dataclass
#class SubPositionValues:
#    name: str
#    position : str

#@dataclass
#class PreviousFrameValues:
#    offset: Tuple[float, float]
#    sub_positions: Any
#    wing_style: str


@dataclass
class PreviousSpriteFileValues:
    #better name!!!!
    frames: Dict[str, List[Any]] 
    sub_positions: str


def deep_merge(base: Dict[str, Any], overrides: Dict[str, Any]) -> Dict[str, Any]:
    for key, value in overrides.items():
        if key in base and isinstance(base[key], dict) and isinstance(value, dict):
            deep_merge(base[key], value)
        else:
            base[key] = value
    return base


def load_config(path: pathlib.Path) -> Dict[str, Any]:
    if path.exists() and path.stat().st_size > 0:
        with path.open("r", encoding="utf-8") as handle:
            overrides = json.load(handle)
        if not isinstance(overrides, dict):
            raise SystemExit(f"Config file {path} must contain a JSON object.")
    else:
        raise SystemExit(f"Config file {path} does not exists or is empty.")
    return overrides


def parse_rgba_color(value: Optional[Any]) -> Optional[Tuple[int, int, int, int]]:
    
    if value is None:
        return None
    
    if isinstance(value, str):
        trimmed = value.strip()
        if trimmed.startswith("#"):
            trimmed = trimmed[1:]
        if len(trimmed) == 6:
            r = int(trimmed[0:2], 16)
            g = int(trimmed[2:4], 16)
            b = int(trimmed[4:6], 16)
            return (r, g, b, 255)
        if len(trimmed) == 8:
            r = int(trimmed[0:2], 16)
            g = int(trimmed[2:4], 16)
            b = int(trimmed[4:6], 16)
            a = int(trimmed[6:8], 16)
            return (r, g, b, a)
        raise SystemExit(f"Unsupported color value: {value}")
    if isinstance(value, Sequence):
        if len(value) == 3:
            r, g, b = value
            return (int(r), int(g), int(b), 255)
        if len(value) == 4:
            r, g, b, a = value
            return (int(r), int(g), int(b), int(a))
    raise SystemExit(f"Unsupported color value: {value}")



def resize_image(image: Image.Image, percent: float) -> Image.Image:
    scale = percent / 100.0
    if scale <= 0:
        raise SystemExit("resize_to_percent must be greater than zero.")
    new_width = max(1, int(round(image.width * scale)))
    new_height = max(1, int(round(image.height * scale)))
    if new_width == image.width and new_height == image.height:
        return image
    return image.resize((new_width, new_height), RESAMPLE_NEAREST)


def remove_color_with_threshold(image: Image.Image, target_color: Tuple[int, int, int, int], threshold: float, reduce_file_size) -> Image.Image:
    im = image.convert("RGBA")
    arr = np.asarray(im).copy()        
    rgb = arr[..., :3].astype(np.int32)   
    alpha = arr[..., 3]

    tr, tg, tb, _ = target_color
    diff = rgb - np.array([tr, tg, tb], dtype=np.int32)
    dist2 = (diff * diff).sum(axis=2)        

    thr2 = int(threshold * threshold)
    mask = (alpha != 0) & (dist2 <= thr2)

    if not reduce_file_size:
        arr[mask, 3] = 0    
    else:
        arr[mask] = 0     

    return Image.fromarray(arr, "RGBA")

def _align_even_box(left: int, top: int, right: int, bottom: int,
                    w: int, h: int) -> Tuple[int,int,int,int]:

    left_aligned  = max(0, left  - (left  % 2))
    top_aligned   = max(0, top   - (top   % 2))
    right_aligned = min(w, right + (right % 2))
    bottom_aligned= min(h, bottom+ (bottom% 2))

    if (right_aligned - left_aligned) % 2 == 1:
        if right_aligned < w: right_aligned += 1
        elif left_aligned > 0: left_aligned -= 1
    if (bottom_aligned - top_aligned) % 2 == 1:
        if bottom_aligned < h: bottom_aligned += 1
        elif top_aligned > 0:  top_aligned -= 1

    return left_aligned, top_aligned, right_aligned, bottom_aligned

def trim_color(image: Image.Image, trim_color: Optional[Tuple[int, int, int, int]], threshold: float) -> Tuple[Image.Image, Tuple[int, int]]:
    img = image if image.mode == "RGBA" else image.convert("RGBA")
    w, h = img.size

    tr, tg, tb, ta = (int(c) for c in trim_color[:4])

    if ta == 0:
   
        alpha = img.getchannel("A")
        bbox = alpha.getbbox()
        if not bbox:
            return image, (0, 0)
        left, top, right, bottom = bbox
    else:
        arr = np.asarray(img, dtype=np.uint8)
        r = arr[..., 0].astype(np.int32)
        g = arr[..., 1].astype(np.int32)
        b = arr[..., 2].astype(np.int32)
        a = arr[..., 3]

        dr = r - tr
        dg = g - tg
        db = b - tb
        da = a.astype(np.int32) - ta

        dist2 = dr*dr + dg*dg + db*db + da*da
        thr2 = int(threshold * threshold)

        neq = dist2 > thr2

        if not np.any(neq):
            return image, (0, 0)

        rows = np.any(neq, axis=1)
        cols = np.any(neq, axis=0)

        top = int(np.argmax(rows))
        bottom = int(h - np.argmax(rows[::-1]))
        left = int(np.argmax(cols))
        right = int(w - np.argmax(cols[::-1]))

    left, top, right, bottom = _align_even_box(left, top, right, bottom, w, h)

    if left == 0 and top == 0 and right == w and bottom == h:
        return image, (0, 0)

    cropped = img.crop((left, top, right, bottom))
    return cropped, (left, top)


#??????
def ensure_even_dimensions(image: Image.Image) -> Image.Image:
    width, height = image.size
    new_width = width + (width % 2)
    new_height = height + (height % 2)
    if new_width == width and new_height == height:
        return image
    padded = Image.new("RGBA", (new_width, new_height), (0, 0, 0, 0))
    padded.paste(image, (0, 0))
    return padded


def ensure_even_value(value: int) -> int:
    return value if value % 2 == 0 else value + 1


def round_half_up(value: float) -> int:
    return int(math.floor(value + 0.5))


def round_away_from_zero(value: float) -> int:
    if value > 0:
        return int(math.ceil(value - 1e-9))
    if value < 0:
        return int(math.floor(value + 1e-9))
    return 0


def append_suffix_to_filename(path: pathlib.Path, suffix: str) -> pathlib.Path:
    if path.suffix:
        return path.with_name(path.stem + suffix + path.suffix)
    return path.with_name(path.name + suffix)


def collect_animation_directories(input_dir: pathlib.Path) -> List[pathlib.Path]:
    directories = [p for p in sorted(input_dir.iterdir()) if p.is_dir()]
    if directories:
        return directories
    return [input_dir]

def load_animation_config(animation_dir: pathlib.Path) -> AnimationConfig:
    config_path = animation_dir / "config.json"

    animation_config_json = json.loads(json.dumps(DEFAULT_ANIMATION_CONFIG))
    animation_config_override_json = load_config(config_path)
    animation_config_json = deep_merge(animation_config_json, animation_config_override_json)

    offset_json = animation_config_json.get("offset")
    if isinstance(offset_json, dict):
        raw_x = offset_json.get("x")
        raw_y = offset_json.get("y")
    else:
        raw_x = raw_y = 0.0

    def _normalize_offset(value: Any) -> float:
        if value is None:
            return 0.0
        
        try:
            scaled = value * 2
        except TypeError as exc:
            raise SystemExit(f"Invalid offset value in {config_path}: {value!r}") from exc
        
        try:
            return float(scaled)
        except (TypeError, ValueError) as exc:
            raise SystemExit(f"Invalid offset value in {config_path}: {value!r}") from exc

    offset = (_normalize_offset(raw_x), _normalize_offset(raw_y))

    recover_json = animation_config_json.get("recover_cropped_offset")

    def _normalize_recover(value: Any) -> bool:
        if isinstance(value, bool):
            return value
        if value is None:
            return True
        return bool(value)     

    recover = (True, True)
    if isinstance(recover_json, dict):
        recover = (
            _normalize_recover(recover_json.get("x")),
            _normalize_recover(recover_json.get("y")),
        )

    regenerate_value = animation_config_json.get("regenerate")
  
    regenerate = True if regenerate_value is None else bool(regenerate_value)

    delay = animation_config_json.get("delay")

    return AnimationConfig(regenerate, delay, offset, recover)


def load_previous_sprite_metadata(path: pathlib.Path, preserve_dirs: set) -> PreviousSpriteFileValues:

    try:
        with path.open("r", encoding="utf-8") as handle:
            payload = json.load(handle)
    except (OSError, json.JSONDecodeError):
        print("Old .sprite file not found!")
        return None

    frames_json = payload.get("Frames")

    previous_frame_values: Dict[str, List[Any]] = {}  

    animations_json = payload.get("NamedAnimations")

    for entry in animations_json:
        name = entry.get("Name")
        if name in preserve_dirs:
            
            frames_field = entry.get("Frames")
            frames_field.strip()
            previous_frame_jsons = list()

            indices = [int(item) for item in frames_field.split(",") if item]
            for i in indices:
                if i < 0 or i >= len(frames_json):
                    continue
                frame_json = frames_json[i] or {}
                #offset_str = frame_entry.get("Offset") or "0 0"
                #sub_positions_raw = frame_entry.get("SubPositions") or None
                # if sub_positions_raw is None:
                #     sub_positions_raw = []
                # sub_position_list: List[SubPositionValues] = []
                # for sub_entry in sub_positions_raw:
                #     if not isinstance(sub_entry, dict):
                #         continue
                #     sub_name_str = sub_entry.get("Name")
                #     sub_positions_str = sub_entry.get("Position")
                #    sub_position_list.append(SubPositionValues(sub_name_str, sub_positions_str))
                #wing_style = frame_entry.get("WingStyle")
                #if wing_style == 0:
                #    wing_style = None
                #try:
                #    offset_tuple = tuple(float(value) for value in offset_str.split())
                #    if len(offset_tuple) != 2:
                #        offset_tuple = (0.0, 0.0)
                #except Exception:
                #    offset_tuple = (0.0, 0.0)

                previous_frame_jsons.append(frame_json)

            previous_frame_values[name] = previous_frame_jsons

    root_sub_positions_str = payload.get("SubPositions")

    return PreviousSpriteFileValues(previous_frame_values, root_sub_positions_str)


def load_existing_sprites(
    target_dir: pathlib.Path,
    previous_frame_values: Any,
    animation_config : AnimationConfig
) -> Optional[List[Dict[str, Any]]]:
    if not target_dir.exists() or not target_dir.is_dir():
        return None

    sprite_paths = sorted(
        path for path in target_dir.iterdir()
        if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
    )
    if not sprite_paths:
        return None

    sprites: List[Dict[str, Any]] = []
    for idx, sprite_path in enumerate(sprite_paths):
        with Image.open(sprite_path) as source_image:
            image = source_image.convert("RGBA")

        sprite_dict = {
            "path": sprite_path,
            "image": image,
            "recover_cropped_offset": (False, False),
            "original_size": image.size,
            "offset": animation_config.offset,
            "trim_offset": (0, 0)
        }
       
        if previous_frame_values != None:
            sprite_dict["old_frame_json"] = previous_frame_values[idx]

        sprites.append(sprite_dict)
    
    return sprites

def collect_sprite_paths(directory: pathlib.Path) -> List[pathlib.Path]:
    return [p for p in sorted(directory.iterdir()) if p.is_file() and p.suffix.lower() in SUPPORTED_EXTENSIONS]


def process_sprites(
    sprite_paths: Sequence[pathlib.Path],
    output_dir: pathlib.Path,
    reduce_file_size: bool,
    subject_config : SubjectConfig,
    animation_config : AnimationConfig
) -> List[Dict[str, Any]]:
    processed: List[Dict[str, Any]] = []
    output_dir.mkdir(parents=True, exist_ok=True)

    for sprite_path in sprite_paths:
        with Image.open(sprite_path) as source_image:
            image = source_image.convert("RGBA")

        can_remove_color = subject_config.background_color is not None and subject_config.remove_background
        if can_remove_color:
            image = remove_color_with_threshold(
                image, subject_config.background_color, subject_config.color_threshold, reduce_file_size
            )

        if not (subject_config.resize_to_percent == 100 or subject_config.resize_to_percent == None):
            image = resize_image(image, subject_config.resize_to_percent)
        original_size = image.size


        trim_offset = (0, 0)
        crop_bg = (0, 0, 0, 0) if can_remove_color else subject_config.background_color
        if subject_config.crop_sprites:
            image, trim_offset = trim_color(image, crop_bg, subject_config.color_threshold)

        image = ensure_even_dimensions(image)


        output_path = output_dir / f"{sprite_path.stem}.png"
        image.save(output_path, format="PNG", optimize=False, compress_level=0)


        processed.append({
            "path": output_path,
            "image": image,
            "trim_offset": trim_offset,
            "original_size": original_size,
            "offset": animation_config.offset,
            "recover_cropped_offset": animation_config.recover_cropped_offset,
        })

    return processed


def layout_for_width(
    sprites: Sequence[Dict[str, Any]],
    gap: int,
    width_limit: int,
) -> Dict[str, Any]:
    if width_limit <= 0:
        raise SystemExit("width_limit must be greater than zero.")
    if not sprites:
        return {"width": 0, "height": 0, "positions": []}
    max_sprite_width = max(sprite["image"].width for sprite in sprites)
    if width_limit < max_sprite_width:
        raise SystemExit("width_limit is smaller than the widest sprite.")

    rows: List[Dict[str, Any]] = []
    current_indices: List[int] = []
    current_width = 0
    current_height = 0

    for index, sprite in enumerate(sprites):
        sprite_width, sprite_height = sprite["image"].size
        projected_width = sprite_width if not current_indices else ensure_even_value(current_width + gap) + sprite_width
        if current_indices and projected_width > width_limit:
            rows.append({
                "indices": current_indices,
                "width": current_width,
                "height": current_height,
            })
            current_indices = []
            current_width = 0
            current_height = 0
            projected_width = sprite_width
        if current_indices:
            current_width = ensure_even_value(current_width + gap)
        current_indices.append(index)
        current_width += sprite_width
        current_height = max(current_height, sprite_height)

    if current_indices:
        rows.append({
            "indices": current_indices,
            "width": current_width,
            "height": current_height,
        })

    sheet_width = max(row["width"] for row in rows)
    sheet_height = 0
    for row_index, row in enumerate(rows):
        if row_index > 0:
            sheet_height = ensure_even_value(sheet_height + gap)
        sheet_height += row["height"]

    positions: List[Optional[Tuple[int, int]]] = [None] * len(sprites)
    y_offset = 0
    for row_index, row in enumerate(rows):
        if row_index > 0:
            y_offset = ensure_even_value(y_offset + gap)
        x_offset = 0
        for item_index, sprite_index in enumerate(row["indices"]):
            sprite_image = sprites[sprite_index]["image"]
            y_position = y_offset + (row["height"] - sprite_image.height)
            positions[sprite_index] = (x_offset, y_position)
            x_offset += sprite_image.width
            if item_index < len(row["indices"]) - 1:
                x_offset = ensure_even_value(x_offset + gap)
        y_offset += row["height"]

    return {"width": sheet_width, "height": sheet_height, "positions": positions}


def auto_layout(
    sprites: Sequence[Dict[str, Any]],
    gap: int,
    max_height: Optional[int] = None,
) -> Dict[str, Any]:
    if not sprites:
        return {"width": 0, "height": 0, "positions": []}
    widths = [sprite["image"].width for sprite in sprites]
    max_width = max(widths)
    total_width = sum(widths)
    candidate_widths = {max_width, total_width + gap * (len(sprites) - 1)}
    prefix = 0
    for index, width in enumerate(widths):
        prefix += width
        candidate_widths.add(max(max_width, prefix + gap * index))

    best_layout: Optional[Dict[str, Any]] = None
    best_score: Optional[Tuple[float, float, float]] = None

    for width_limit in sorted(candidate_widths):
        try:
            layout = layout_for_width(sprites, gap, int(round(width_limit)))
        except ValueError:
            continue
        if max_height is not None and layout["height"] > max_height:
            continue
        diff = abs(layout["width"] - layout["height"])
        area = float(layout["width"] * max(layout["height"], 1))
        height_gap = abs(max_height - layout["height"]) if max_height is not None else 0.0
        score = (height_gap, diff, area)
        if best_score is None or score < best_score:
            best_layout = layout
            best_score = score

    if best_layout is None:
        raise SystemExit("Unable to find an automatic layout that satisfies the constraints.")
    return best_layout


def select_layout(
    sprites: Sequence[Dict[str, Any]],
    forced_width: Optional[int],
    forced_height: Optional[int],
) -> Dict[str, Any]:
    gap = LAYOUT_GAP
    if not sprites:
        canvas_width = forced_width or 0
        canvas_height = forced_height or 0
        return {
            "layout_width": canvas_width,
            "layout_height": canvas_height,
            "canvas_width": canvas_width,
            "canvas_height": canvas_height,
            "positions": [],
        }
    if forced_width is not None:
        layout = layout_for_width(sprites, gap, forced_width)
        if forced_height is not None and layout["height"] > forced_height:
            raise SystemExit("Sprites do not fit within the requested sheet height.")
        canvas_width = forced_width
        canvas_height = forced_height if forced_height is not None else layout["height"]
        return {
            "layout_width": layout["width"],
            "layout_height": layout["height"],
            "canvas_width": canvas_width,
            "canvas_height": canvas_height,
            "positions": list(layout["positions"]),
        }
    layout = auto_layout(sprites, gap, max_height=forced_height)
    canvas_height = forced_height if forced_height is not None else layout["height"]
    return {
        "layout_width": layout["width"],
        "layout_height": layout["height"],
        "canvas_width": layout["width"],
        "canvas_height": canvas_height,
        "positions": list(layout["positions"]),
    }

def create_sprite_sheet(
    sprites: Sequence[Dict[str, Any]],
    positions: Sequence[Tuple[int, int]],
    canvas_size: Tuple[int, int],
) -> Image.Image:
    width, height = canvas_size
    if width <= 1 or height <= 1:
        raise SystemExit("Sprites don't exist.")
    sheet = Image.new("RGBA", (width, height))
    for sprite, position in zip(sprites, positions):
        if position is None:
            continue
        sheet.paste(sprite["image"], position, sprite["image"])
    return sheet


def export_sprite_metadata(
    sprites: Sequence[Dict[str, Any]],
    positions: Sequence[Tuple[int, int]],
    source_canvas: Tuple[int, int],
    target_canvas: Tuple[int, int],
    animations: Sequence[Dict[str, Any]],
    sub_positions: str
) -> Dict[str, Any]:
    source_width, source_height = source_canvas
    target_width, target_height = target_canvas
    scale_x = target_width / source_width if source_width else 1.0
    scale_y = target_height / source_height if source_height else 1.0

    frames: List[Dict[str, Any]] = []
    for sprite, position in zip(sprites, positions):
        left, top = position
        width, height = sprite["image"].size
        original_width, original_height = sprite["original_size"]
       
        left_scaled = round_half_up(left * scale_x)
        top_scaled = round_half_up(top * scale_y)
        right_scaled = round_away_from_zero((left + width) * scale_x)
        bottom_scaled = round_away_from_zero((top + height) * scale_y)

        old_frame_json = sprite.get("old_frame_json", None)
        if old_frame_json == None:
            trim_left, trim_top = sprite["trim_offset"]
            recover_cropped_offset = sprite["recover_cropped_offset"]
            recover_x, recover_y = recover_cropped_offset
            if not recover_x:
                trim_left = 0
                original_width = abs(right_scaled - left_scaled) * 2
            if not recover_y:
                trim_top = 0
                original_height = abs(bottom_scaled - top_scaled) * 2

            extra_offset_x, extra_offset_y = sprite["offset"]
            origin_offset_x = original_width / 2.0 - trim_left + extra_offset_x
            origin_offset_y = original_height - trim_top + extra_offset_y
            scaled_offset_x = round_away_from_zero(origin_offset_x * scale_x)
            scaled_offset_y = round_away_from_zero(origin_offset_y * scale_y)
            
            offset_text = f"{scaled_offset_x} {scaled_offset_y}"

            frame_values = {
                "Offset": offset_text
            }
        
        else:
            print("Using old frames.")
            frame_values = old_frame_json

        frame_values["Rect"] = f"{left_scaled} {top_scaled} {right_scaled} {bottom_scaled}"

        # sub_position_list : List[Dict[str, Any]] = []
        #frame_sub_positions = sprite.get("sub_positions")
        # if frame_sub_positions != None:        
        #     for sub_position in frame_sub_positions:
        #         sub_position_list.append({
        #             "Name": sub_position.name,
        #             "Position": sub_position.position
        #         })
        
        #if frame_sub_positions != None:
        #    frame_values["SubPositions"] = frame_sub_positions


        frames.append(frame_values)

    frame_index = 0
    named_animations = []
    for animation in animations:
        frame_str = ",".join(str(index) for index in animation["frames"])
        named_animations.append({
            "Name": animation["name"],
            "Frames": frame_str,
            "Delay": int(animation["delay"]),
        })
        frame_index += len(sprites)

    payload = {
        "Frames": frames,
        "NamedAnimations": named_animations,
        "SubPositions": sub_positions,
        "Version": "Neoarc's Sprite v2.0",
    }

    return payload

def main() -> None:
    base_config = load_config(pathlib.Path(CONFIG_PATH))
    subject_name = base_config.get("subject")
    if not subject_name:
        raise SystemExit("The 'subject' field must be specified in the main config.json.")

    subject_config_json_path = pathlib.Path(subject_name) / "config.json"
    subject_config_json = json.loads(json.dumps(DEFAULT_SUBJECT_CONFIG))
    subject_config_json_override = load_config(subject_config_json_path)
    subject_config_json = deep_merge(subject_config_json, subject_config_json_override)

    resize_to_percent = float(subject_config_json.get("resize_to_percent"))
    background_color = parse_rgba_color(subject_config_json.get("background_color"))
    color_threshold = float(subject_config_json.get("color_threshold"))
    remove_background = bool(subject_config_json.get("remove_background"))
    crop_sprites = bool(subject_config_json.get("crop_sprites"))

    sheet_config = subject_config_json.get("sheet")
    forced_width = sheet_config.get("width")
    forced_height = sheet_config.get("height")
    forced_width = int(forced_width) if forced_width is not None else None
    forced_height = int(forced_height) if forced_height is not None else None

    subject_config = SubjectConfig(resize_to_percent, background_color, color_threshold, remove_background, crop_sprites, (forced_width, forced_height))

    reduce_file_size = bool(subject_config_json.get("reduce_file_size"))

    input_dir = pathlib.Path(subject_name) / "raw"

    if not input_dir.exists():
        raise SystemExit(f"Input directory not found: {input_dir}")

    output_dir = pathlib.Path(subject_name) / "generated"

    spritesheet_path = output_dir / (subject_name + ".png")
    spritesheet_path_2x = output_dir / (subject_name + "@2x.png")
    sprite_file_path = output_dir / (subject_name + ".sprite")

    animation_dirs = collect_animation_directories(input_dir)

    animation_config_by_dir: Dict[pathlib.Path, AnimationConfig] = {}
    preserve_dirs = set()
    for animation_dir in animation_dirs:
        animation_config = load_animation_config(animation_dir)
        animation_config_by_dir[animation_dir] = animation_config
        if not animation_config.regenerate:
            preserve_dirs.add(animation_dir.name)

    sub_positions = ""
    if len(preserve_dirs) > 0:
        previous_sprite_file = load_previous_sprite_metadata(sprite_file_path, preserve_dirs)
        if previous_sprite_file != None and previous_sprite_file.sub_positions != None:
            sub_positions = previous_sprite_file.sub_positions
        

    processed_sprites: List[Dict[str, Any]] = []

    animations_meta: List[Dict[str, Any]] = []
    frame_index = 0

    if output_dir.exists():
        for child in list(output_dir.iterdir()):
            if child.is_dir() and child.name in preserve_dirs:
                continue
            if child.is_dir():
                shutil.rmtree(child, ignore_errors=True)

    for animation_dir in animation_dirs:
        animation_name = animation_dir.name
        animation_config = animation_config_by_dir[animation_dir]   

        sprites: Optional[List[Dict[str, Any]]]
        if animation_config.regenerate:
            sprite_paths = collect_sprite_paths(animation_dir)
  
            sprites = process_sprites(
                sprite_paths,
                output_dir / animation_name,
                reduce_file_size,
                subject_config,
                animation_config
            )
        else:

            previous_frame_values = None
            if previous_sprite_file != None:
                previous_frame_values = previous_sprite_file.frames[animation_name]
            sprites = load_existing_sprites(
                output_dir / animation_name,
                previous_frame_values,
                animation_config
            )

        processed_sprites.extend(sprites)

        frame_range = list(range(frame_index, frame_index + len(sprites)))

        animations_meta.append({
            "name": animation_name,
            "frames": frame_range,
            "delay": animation_config.delay,
        })
        frame_index += len(sprites)


    layout_info = select_layout(processed_sprites, forced_width, forced_height)
    final_positions = layout_info["positions"]
    if any(position is None for position in final_positions):
        raise SystemExit("Failed to generate positions for every sprite.")

    canvas_size = (layout_info["canvas_width"], layout_info["canvas_height"])

    spritesheet_path.parent.mkdir(parents=True, exist_ok=True)
    spritesheet_path_2x.parent.mkdir(parents=True, exist_ok=True)


    sheet_image = create_sprite_sheet(
        processed_sprites,
        final_positions,
        canvas_size,
    )

    half_width = max(1, (sheet_image.width + 1) // 2)
    half_height = max(1, (sheet_image.height + 1) // 2)
    half_canvas_size = (half_width, half_height)
    sheet_half = sheet_image.resize(half_canvas_size, RESAMPLE_NEAREST)

    payload = export_sprite_metadata(
        processed_sprites,
        final_positions,
        canvas_size,
        half_canvas_size,
        animations_meta,
        sub_positions
    )

    if output_dir.exists():
        for child in list(output_dir.iterdir()):
            if not child.is_dir():
                try:
                    child.unlink()
                except FileNotFoundError:
                    pass

    output_dir.mkdir(parents=True, exist_ok=True)

    sheet_image.save(spritesheet_path_2x, format="PNG", optimize=reduce_file_size)

    sheet_half.save(spritesheet_path)

    with sprite_file_path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)

    print(f"Processed {len(processed_sprites)} sprites into {output_dir}.")
    print(f"High-res sprite sheet saved to {spritesheet_path_2x.resolve()} with size {canvas_size[0]}x{canvas_size[1]} pixels.")
    print(f"Half-res sprite sheet saved to {spritesheet_path.resolve()} with size {half_canvas_size[0]}x{half_canvas_size[1]} pixels.")
    print(f"Offset metadata saved to {sprite_file_path.resolve()}.")
if __name__ == "__main__":
    main()
