from __future__ import annotations

import argparse
import json
import re
import sys
import time
from html import unescape
from io import BytesIO
from pathlib import Path
from urllib.parse import unquote
from urllib.request import Request, urlopen

try:
    from PIL import Image
except ImportError:
    Image = None

USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
ROW_IMAGE_NAME_PATTERN = re.compile(
    r"<tr>\s*<th>.*?(?:<a[^>]*>)?<span\s+id=\"(?P<slug>[^\"]+)\"\s+class=\"attach\">(?P<name>.*?)</span>.*?<a\s+href=\"(?P<href>https://static\.wikia\.nocookie\.net/finalfantasy/images/[^\"]+_from_FFVIIEC\.(?:png|jpg|jpeg|webp)[^\"]*)\"",
    re.IGNORECASE | re.DOTALL,
)
WINDOWS_RESERVED_NAMES = {
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
}


def fetch_bytes(url: str, timeout: float = 30.0) -> tuple[bytes, str | None]:
    request = Request(url, headers={"User-Agent": USER_AGENT, "Referer": "https://finalfantasy.fandom.com/"})
    with urlopen(request, timeout=timeout) as response:
        return response.read(), response.headers.get_content_type()


def load_text(source_file: Path) -> str:
    return source_file.read_text(encoding="utf-8", errors="replace")


def normalize_url(url: str) -> str:
    value = unescape(url.replace("\\/", "/")).strip()
    return value.rstrip('"\'')


def strip_html_tags(value: str) -> str:
    return re.sub(r"<[^>]+>", "", value)


def sanitize_filename(name: str) -> str:
    cleaned = unquote(name)
    cleaned = re.sub(r"[<>:\"/\\|?*]+", " ", cleaned)
    cleaned = re.sub(r"\s+", " ", cleaned).strip(" .")
    if not cleaned:
        cleaned = "weapon_image"
    if cleaned.upper() in WINDOWS_RESERVED_NAMES:
        cleaned = f"_{cleaned}"
    return cleaned


def clean_weapon_name(name: str) -> str:
    cleaned = unescape(name)
    cleaned = cleaned.replace("_from_FFVIIEC", "")
    cleaned = cleaned.replace("_", " ")
    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    return sanitize_filename(cleaned)


def extract_structured_entries(text: str) -> list[tuple[str, str]]:
    entries: list[tuple[str, str]] = []
    for match in ROW_IMAGE_NAME_PATTERN.finditer(text):
        url = normalize_url(match.group("href"))
        name = clean_weapon_name(strip_html_tags(match.group("name")))
        if url and name:
            entries.append((url, name))
    return entries


def is_png_bytes(data: bytes) -> bool:
    return data.startswith(PNG_SIGNATURE)


def verify_png_file(path: Path) -> None:
    header = path.read_bytes()[: len(PNG_SIGNATURE)]
    if header != PNG_SIGNATURE:
        raise RuntimeError(f"Saved file is not PNG: {path}")


def save_as_png(destination_without_extension: Path, data: bytes, content_type: str | None) -> Path:
    destination = destination_without_extension.with_suffix(".png")
    if is_png_bytes(data):
        destination.write_bytes(data)
        verify_png_file(destination)
        return destination

    if Image is None:
        detail = content_type or "unknown content type"
        raise RuntimeError(f"Downloaded image is not PNG ({detail}) and Pillow is not installed")

    with Image.open(BytesIO(data)) as image:
        output = image
        if image.mode not in ("RGBA", "RGB", "L"):
            output = image.convert("RGBA")
        output.save(destination, format="PNG")

    verify_png_file(destination)
    return destination


def unique_destination(base_path: Path) -> Path:
    if not base_path.exists():
        return base_path
    counter = 2
    while True:
        candidate = base_path.with_name(f"{base_path.stem} ({counter}){base_path.suffix}")
        if not candidate.exists():
            return candidate
        counter += 1


def collect_entries(source_file: Path) -> list[tuple[str, str]]:
    text = load_text(source_file)
    structured_entries = extract_structured_entries(text)
    if not structured_entries:
        return []

    found: dict[str, str] = {}
    for url, name in structured_entries:
        found.setdefault(url, name)
    return sorted(found.items(), key=lambda item: item[1].lower())


def download_urls(
    entries: list[tuple[str, str]],
    output_dir: Path,
    delay_seconds: float,
    skip_existing: bool,
) -> tuple[int, list[dict[str, str]]]:
    output_dir.mkdir(parents=True, exist_ok=True)
    downloaded = 0
    index_payload: list[dict[str, str]] = []
    assigned_destinations: set[Path] = set()

    for index, (url, base_name) in enumerate(entries, start=1):
        destination = output_dir / f"{base_name}.png"

        if skip_existing and destination.exists():
            print(f"[skip {index}/{len(entries)}] {base_name}")
            index_payload.append({"name": base_name, "url": url, "file": destination.name})
            continue

        candidate_path = destination
        if not skip_existing:
            while candidate_path in assigned_destinations:
                candidate_path = unique_destination(candidate_path)

        try:
            data, content_type = fetch_bytes(url)
            saved_path = save_as_png(candidate_path.with_suffix(""), data, content_type)
            assigned_destinations.add(saved_path)
            index_payload.append({"name": base_name, "url": url, "file": saved_path.name})
            downloaded += 1
            print(f"[ok   {index}/{len(entries)}] {base_name} -> {saved_path.name}")
        except Exception as exc:
            print(f"[fail {index}/{len(entries)}] {base_name} -> {exc}", file=sys.stderr)

        if delay_seconds > 0:
            time.sleep(delay_seconds)

    return downloaded, index_payload


def write_index(output_dir: Path, rows: list[dict[str, str]]) -> None:
    index_path = output_dir / "download_index.json"
    index_path.write_text(json.dumps(rows, indent=2, ensure_ascii=False), encoding="utf-8")


def fix_existing_names(source_file: Path, output_dir: Path) -> int:
    entries = collect_entries(source_file)
    if not entries:
        return 0

    output_dir.mkdir(parents=True, exist_ok=True)
    desired_names_by_url = {url: name for url, name in entries}
    index_path = output_dir / "download_index.json"
    rows: list[dict[str, str]]
    if index_path.exists():
        rows = json.loads(index_path.read_text(encoding="utf-8"))
    else:
        rows = [{"name": name, "url": url, "file": f"{name}.png"} for url, name in entries]

    renamed = 0
    for row in rows:
        url = row.get("url", "")
        desired_name = desired_names_by_url.get(url)
        if not desired_name:
            continue

        desired_file = f"{desired_name}.png"
        current_file = str(row.get("file", "")).strip()
        current_path = output_dir / current_file if current_file else None
        target_path = output_dir / desired_file

        if current_path and current_path.exists() and current_path.resolve() != target_path.resolve():
            if target_path.exists():
                raise RuntimeError(f"Cannot rename {current_path.name} because {target_path.name} already exists")
            current_path.rename(target_path)
            renamed += 1

        row["name"] = desired_name
        row["file"] = desired_file

    write_index(output_dir, rows)
    return renamed


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Extract and download FFVIIEC weapon images from a saved Fandom weapons page.")
    parser.add_argument(
        "--source-file",
        type=Path,
        default=script_dir / "weapons.html",
        help="Path to the saved weapons.html file. Defaults to this script's sibling weapons.html.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=script_dir / "downloaded_weapon_images",
        help="Folder where downloaded PNG images and download_index.json will be written.",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.15,
        help="Delay in seconds between downloads. Default: 0.15",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Overwrite existing files instead of skipping them.",
    )
    parser.add_argument(
        "--fix-existing-names",
        action="store_true",
        help="Rename already-downloaded files to the current normalized names and rewrite download_index.json without redownloading.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    source_file = args.source_file.resolve()
    output_dir = args.output_dir.resolve()

    if not source_file.exists() or not source_file.is_file():
        print(f"Source file does not exist: {source_file}", file=sys.stderr)
        return 1

    if args.fix_existing_names:
        renamed = fix_existing_names(source_file, output_dir)
        print(f"Renamed {renamed} file(s) in {output_dir}")
        return 0

    entries = collect_entries(source_file)
    if not entries:
        print(
            "No Fandom weapon image rows were found. Make sure weapons.html is saved and still contains the weapon table with static.wikia.nocookie.net image links.",
            file=sys.stderr,
        )
        return 2

    print(f"Found {len(entries)} unique weapon image URLs")
    downloaded, index_rows = download_urls(
        entries,
        output_dir=output_dir,
        delay_seconds=max(args.delay, 0.0),
        skip_existing=not args.overwrite,
    )
    write_index(output_dir, index_rows)
    print(f"Downloaded {downloaded} file(s) to {output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
