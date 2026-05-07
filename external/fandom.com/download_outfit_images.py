from __future__ import annotations

import argparse
import json
import mimetypes
import re
import sys
import time
from html import unescape
from io import BytesIO
from pathlib import Path
from typing import Iterable
from urllib.parse import unquote, urlparse
from urllib.request import Request, urlopen

try:
    from PIL import Image
except ImportError:
    Image = None

USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
IMAGE_URL_PATTERN = re.compile(
    r"https://static\.wikia\.nocookie\.net/finalfantasy/images/[^\"'<>\s)]+",
    re.IGNORECASE,
)
ROW_IMAGE_NAME_PATTERN = re.compile(
    r"<tr>.*?<a\s+href=\"(?P<href>https://static\.wikia\.nocookie\.net/finalfantasy/images/[^\"]+_gear_from_FFVIIEC\.(?:png|jpg|jpeg|webp)[^\"]*)\"[^>]*>.*?</td>\s*<td>(?:<a[^>]*>)?<span\s+id=\"(?P<slug>[^\"]+)\"\s+class=\"attach\">(?P<name>.*?)</span>",
    re.IGNORECASE | re.DOTALL,
)
SUPPORTED_SOURCE_EXTENSIONS = (".png", ".jpg", ".jpeg", ".webp")
WINDOWS_RESERVED_NAMES = {
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
}


def fetch_bytes(url: str, timeout: float = 30.0) -> tuple[bytes, str | None]:
    request = Request(url, headers={"User-Agent": USER_AGENT, "Referer": "https://finalfantasy.fandom.com/"})
    with urlopen(request, timeout=timeout) as response:
        return response.read(), response.headers.get_content_type()


def load_text_files(search_root: Path) -> Iterable[tuple[Path, str]]:
    patterns = ("*.html", "*.htm", "*.txt", "*.json")
    seen: set[Path] = set()
    for pattern in patterns:
        for path in search_root.rglob(pattern):
            if path in seen or not path.is_file():
                continue
            seen.add(path)
            try:
                yield path, path.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue


def normalize_url(url: str) -> str:
    value = unescape(url.replace("\\/", "/")).strip()
    return value.rstrip('"\'')


def extract_image_urls(text: str) -> set[str]:
    return {normalize_url(match) for match in IMAGE_URL_PATTERN.findall(text)}


def strip_html_tags(value: str) -> str:
    return re.sub(r"<[^>]+>", "", value)


def clean_outfit_name(name: str) -> str:
    cleaned = unescape(name)
    cleaned = cleaned.replace("_gear_from_FFVIIEC", "")
    cleaned = cleaned.replace("_", " ")
    cleaned = re.sub(r"\s+", " ", cleaned).strip()
    return sanitize_filename(cleaned)


def extract_structured_entries(text: str) -> list[tuple[str, str]]:
    entries: list[tuple[str, str]] = []
    for match in ROW_IMAGE_NAME_PATTERN.finditer(text):
        url = normalize_url(match.group("href"))
        name = clean_outfit_name(strip_html_tags(match.group("name")))
        if url and name:
            entries.append((url, name))
    return entries


def sanitize_filename(name: str) -> str:
    cleaned = unquote(name)
    cleaned = re.sub(r"[<>:\"/\\|?*]+", "_", cleaned)
    cleaned = re.sub(r"\s+", " ", cleaned).strip(" .")
    if not cleaned:
        cleaned = "outfit_image"
    if cleaned.upper() in WINDOWS_RESERVED_NAMES:
        cleaned = f"_{cleaned}"
    return cleaned


def infer_name_from_url(url: str) -> str:
    path = urlparse(url).path
    candidate = Path(path).name
    if candidate.lower().startswith("scale-to-width") or not candidate:
        parts = [part for part in path.split("/") if part]
        for part in reversed(parts):
            lower = part.lower()
            if lower.startswith("scale-to-width") or lower == "latest" or lower == "revision":
                continue
            if Path(part).suffix.lower() in SUPPORTED_SOURCE_EXTENSIONS:
                candidate = part
                break
    stem = Path(candidate).stem or candidate
    return clean_outfit_name(stem)


def guess_extension(content_type: str | None, data: bytes) -> str:
    if content_type:
        guessed = mimetypes.guess_extension(content_type.split(";", 1)[0].strip())
        if guessed:
            return ".jpg" if guessed == ".jpe" else guessed
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return ".png"
    if data.startswith(b"\xff\xd8\xff"):
        return ".jpg"
    if data.startswith((b"GIF87a", b"GIF89a")):
        return ".gif"
    if data.startswith(b"RIFF") and data[8:12] == b"WEBP":
        return ".webp"
    return ".bin"


def save_original_file(destination_without_extension: Path, data: bytes, content_type: str | None) -> Path:
    extension = guess_extension(content_type, data)
    destination = destination_without_extension.with_suffix(extension)
    destination.write_bytes(data)
    return destination


def convert_image(destination_without_extension: Path, data: bytes, requested_format: str) -> Path:
    if Image is None:
        raise RuntimeError("Pillow is not installed")

    with Image.open(BytesIO(data)) as image:
        output = image
        if requested_format == "jpg":
            if image.mode not in ("RGB", "L"):
                output = image.convert("RGB")
            destination = destination_without_extension.with_suffix(".jpg")
            output.save(destination, format="JPEG", quality=95)
            return destination

        if image.mode not in ("RGBA", "RGB", "L"):
            output = image.convert("RGBA")
        destination = destination_without_extension.with_suffix(".png")
        output.save(destination, format="PNG")
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


def collect_entries(source_dir: Path) -> list[tuple[str, str]]:
    found: dict[str, str] = {}
    for path, text in load_text_files(source_dir):
        structured_entries = extract_structured_entries(text)
        if structured_entries:
            print(f"[scan] {path.relative_to(source_dir)} -> {len(structured_entries)} structured outfit rows")
            for url, name in structured_entries:
                found.setdefault(url, name)
            continue

        urls = {
            url
            for url in extract_image_urls(text)
            if "_gear_from_FFVIIEC." in url
        }
        if urls:
            print(f"[scan] {path.relative_to(source_dir)} -> {len(urls)} fallback image URLs")
            for url in urls:
                found.setdefault(url, infer_name_from_url(url))

    return sorted(found.items(), key=lambda item: item[1].lower())


def download_urls(
    entries: list[tuple[str, str]],
    output_dir: Path,
    preferred_format: str,
    delay_seconds: float,
    skip_existing: bool,
) -> tuple[int, list[dict[str, str]]]:
    output_dir.mkdir(parents=True, exist_ok=True)
    downloaded = 0
    index_payload: list[dict[str, str]] = []
    assigned_destinations: set[Path] = set()

    for index, (url, base_name) in enumerate(entries, start=1):
        destination_without_extension = output_dir / base_name

        if skip_existing and any(destination_without_extension.with_suffix(ext).exists() for ext in (".png", ".jpg", ".jpeg", ".webp", ".gif", ".bin")):
            print(f"[skip {index}/{len(entries)}] {base_name}")
            continue

        try:
            data, content_type = fetch_bytes(url)
            target_extension = (
                ".png" if preferred_format == "png"
                else ".jpg" if preferred_format == "jpg"
                else guess_extension(content_type, data)
            )
            if preferred_format in {"png", "jpg"} and Image is None:
                target_extension = guess_extension(content_type, data)

            candidate_path = destination_without_extension.with_suffix(target_extension)
            if not skip_existing:
                while candidate_path in assigned_destinations:
                    candidate_path = unique_destination(candidate_path)

            write_base = candidate_path.with_suffix("")

            if preferred_format in {"png", "jpg"} and Image is not None:
                saved_path = convert_image(write_base, data, preferred_format)
            elif preferred_format in {"png", "jpg"} and Image is None:
                saved_path = save_original_file(write_base, data, content_type)
                print(f"[warn {index}/{len(entries)}] Pillow not installed; kept original format for {base_name}")
            else:
                saved_path = save_original_file(write_base, data, content_type)

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


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="Extract and download FFVIIEC outfit images from saved Fandom HTML.")
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=script_dir,
        help="Folder to scan for gear.html or other saved HTML files. Defaults to this script's folder.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=script_dir / "downloaded_outfit_images",
        help="Folder where downloaded images and download_index.json will be written.",
    )
    parser.add_argument(
        "--format",
        choices=("png", "jpg", "original"),
        default="png",
        help="Preferred saved format. Uses Pillow if installed for png/jpg conversion. Default: png",
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
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    source_dir = args.source_dir.resolve()
    output_dir = args.output_dir.resolve()

    if not source_dir.exists() or not source_dir.is_dir():
        print(f"Source directory does not exist: {source_dir}", file=sys.stderr)
        return 1

    entries = collect_entries(source_dir)
    if not entries:
        print(
            "No Fandom outfit image URLs were found. Make sure gear.html is saved inside this folder and contains static.wikia.nocookie.net image links.",
            file=sys.stderr,
        )
        return 2

    if args.format in {"png", "jpg"} and Image is None:
        print("[note] Pillow is not installed. Images will be saved in their original downloaded format.")

    print(f"Found {len(entries)} unique outfit image URLs")
    downloaded, index_rows = download_urls(
        entries,
        output_dir=output_dir,
        preferred_format=args.format,
        delay_seconds=max(args.delay, 0.0),
        skip_existing=not args.overwrite,
    )
    write_index(output_dir, index_rows)
    print(f"Downloaded {downloaded} file(s) to {output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
