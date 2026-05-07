from __future__ import annotations

import argparse
import json
import mimetypes
import re
import sys
import time
from html import unescape
from pathlib import Path
from typing import Iterable
from urllib.parse import parse_qs, quote, unquote, urljoin, urlparse
from urllib.request import Request, urlopen

BASE_PAGE_URL = "https://databat.net/"
LIST_WEAPONS_URL = "https://databat.net/lib/listWeapon.php"
BASE_IMAGE_URL = "https://databat.net/lib/getWeaponImage.php"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
URL_PATTERN = re.compile(
    r"(?:https://databat\.net)?(?:/|\\/)+(?:lib/)?getWeaponImage\.php\?q=([^\"'<>\s&]+(?:&[^\"'<>\s]*)?)",
    re.IGNORECASE,
)
WINDOWS_RESERVED_NAMES = {
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
}


def fetch_text(url: str, timeout: float = 30.0) -> str:
    request = Request(url, headers={"User-Agent": USER_AGENT})
    with urlopen(request, timeout=timeout) as response:
        charset = response.headers.get_content_charset() or "utf-8"
        return response.read().decode(charset, errors="replace")


def fetch_json_post(url: str, timeout: float = 30.0) -> object:
    request = Request(
        url,
        data=b"",
        headers={
            "User-Agent": USER_AGENT,
            "X-Requested-With": "XMLHttpRequest",
            "Referer": BASE_PAGE_URL,
        },
    )
    with urlopen(request, timeout=timeout) as response:
        charset = response.headers.get_content_charset() or "utf-8"
        return json.loads(response.read().decode(charset, errors="replace"))


def fetch_bytes(url: str, timeout: float = 30.0) -> tuple[bytes, str | None]:
    request = Request(url, headers={"User-Agent": USER_AGENT, "Referer": BASE_PAGE_URL})
    with urlopen(request, timeout=timeout) as response:
        return response.read(), response.headers.get_content_type()


def load_text_files(search_root: Path) -> Iterable[tuple[Path, str]]:
    patterns = ("*.html", "*.htm", "*.js", "*.download", "*.txt", "*.json")
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


def normalize_candidate(candidate: str) -> str:
    value = candidate.replace("\\/", "/")
    value = unescape(value)
    value = value.strip()
    if value.lower().startswith("http://") or value.lower().startswith("https://"):
        return value
    if value.lower().startswith("lib/getweaponimage.php"):
        return urljoin(BASE_PAGE_URL, candidate)
    if value.lower().startswith("/lib/getweaponimage.php"):
        return urljoin(BASE_PAGE_URL, candidate)
    if value.lower().startswith("getweaponimage.php"):
        return f"{BASE_IMAGE_URL}?{candidate.split('?', 1)[1]}"
    return candidate


def extract_weapon_image_urls(text: str) -> set[str]:
    matches = set()
    for raw_match in URL_PATTERN.findall(text):
        query = raw_match.strip()
        if not query:
            continue
        matches.add(f"{BASE_IMAGE_URL}?{query}")

    for direct in re.findall(r"(?:https://databat\.net)?/(?:lib/)?getWeaponImage\.php\?[^\"'<>\s]+", text, flags=re.IGNORECASE):
        matches.add(normalize_candidate(direct))

    return matches


def build_weapon_image_url(weapon_name: str) -> str:
    return f"{BASE_IMAGE_URL}?q={quote(weapon_name, safe='')}"


def collect_urls_from_api() -> list[str]:
    payload = fetch_json_post(LIST_WEAPONS_URL)
    if not isinstance(payload, dict):
        return []

    rows = payload.get("data")
    if not isinstance(rows, list):
        return []

    urls = {
        build_weapon_image_url(str(row.get("W_NAME", "")).strip())
        for row in rows
        if isinstance(row, dict) and str(row.get("W_NAME", "")).strip()
    }

    return sorted(urls, key=lambda item: unquote(get_query_value(item, "q")).lower())


def get_query_value(url: str, key: str) -> str:
    parsed = urlparse(url)
    values = parse_qs(parsed.query).get(key)
    if not values:
        return ""
    return values[0]


def sanitize_filename(name: str) -> str:
    cleaned = unquote(name)
    cleaned = re.sub(r"[<>:\"/\\|?*]+", "_", cleaned)
    cleaned = re.sub(r"\s+", " ", cleaned).strip(" .")
    if not cleaned:
        cleaned = "weapon_image"
    if cleaned.upper() in WINDOWS_RESERVED_NAMES:
        cleaned = f"_{cleaned}"
    return cleaned


def guess_extension(content_type: str | None, data: bytes) -> str:
    if content_type:
        guessed = mimetypes.guess_extension(content_type.split(";", 1)[0].strip())
        if guessed:
            return guessed
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return ".png"
    if data.startswith(b"\xff\xd8\xff"):
        return ".jpg"
    if data.startswith((b"GIF87a", b"GIF89a")):
        return ".gif"
    if data.startswith(b"RIFF") and data[8:12] == b"WEBP":
        return ".webp"
    return ".bin"


def save_index(urls: list[str], output_dir: Path) -> None:
    index_path = output_dir / "download_index.json"
    payload = [
        {
            "url": url,
            "q": get_query_value(url, "q"),
        }
        for url in urls
    ]
    index_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")


def collect_urls(local_root: Path, fetch_live: bool) -> list[str]:
    found: set[str] = set()

    if fetch_live:
        try:
            api_urls = collect_urls_from_api()
            if api_urls:
                print(f"[live-api] listWeapon.php -> {len(api_urls)} candidate URLs")
                found.update(api_urls)
            else:
                print("[live-api] listWeapon.php returned no weapon image URLs")
        except Exception as exc:
            print(f"[warn] failed to fetch {LIST_WEAPONS_URL}: {exc}", file=sys.stderr)

    for path, text in load_text_files(local_root):
        urls = extract_weapon_image_urls(text)
        if urls:
            print(f"[scan] {path.relative_to(local_root)} -> {len(urls)} candidate URLs")
            found.update(urls)

    if fetch_live and not found:
        try:
            html = fetch_text(BASE_PAGE_URL)
            live_urls = extract_weapon_image_urls(html)
            if live_urls:
                print(f"[live] homepage -> {len(live_urls)} candidate URLs")
                found.update(live_urls)
            else:
                print("[live] homepage fetched, but no getWeaponImage.php?q=... URLs were found")
        except Exception as exc:
            print(f"[warn] failed to fetch {BASE_PAGE_URL}: {exc}", file=sys.stderr)

    return sorted(found, key=lambda item: unquote(get_query_value(item, "q")).lower())


def download_urls(urls: list[str], output_dir: Path, delay_seconds: float, skip_existing: bool) -> int:
    output_dir.mkdir(parents=True, exist_ok=True)
    save_index(urls, output_dir)

    downloaded = 0
    for index, url in enumerate(urls, start=1):
        q_value = get_query_value(url, "q")
        display_name = unquote(q_value) if q_value else f"weapon_{index:04d}"
        safe_name = sanitize_filename(display_name)

        try:
            data, content_type = fetch_bytes(url)
            extension = guess_extension(content_type, data)
            destination = output_dir / f"{safe_name}{extension}"

            if destination.exists() and skip_existing:
                print(f"[skip {index}/{len(urls)}] {display_name} -> {destination.name}")
                continue

            destination.write_bytes(data)
            downloaded += 1
            print(f"[ok   {index}/{len(urls)}] {display_name} -> {destination.name}")
        except Exception as exc:
            print(f"[fail {index}/{len(urls)}] {display_name} -> {exc}", file=sys.stderr)

        if delay_seconds > 0:
            time.sleep(delay_seconds)

    return downloaded


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(
        description="Extract databat weapon image URLs and download them locally.",
    )
    parser.add_argument(
        "--source-dir",
        type=Path,
        default=script_dir,
        help="Folder to scan for saved databat HTML/JS files. Defaults to this script's folder.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=script_dir / "downloaded_weapon_images",
        help="Folder where images and download_index.json will be written.",
    )
    parser.add_argument(
        "--local-only",
        action="store_true",
        help="Only scan local files and do not fetch https://databat.net/ as a fallback.",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.15,
        help="Delay in seconds between image downloads. Default: 0.15",
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

    urls = collect_urls(source_dir, fetch_live=not args.local_only)
    if not urls:
        print(
            "No https://databat.net/lib/getWeaponImage.php?q=... URLs were found. "
            "If your saved HTML rewrote them to local files, rerun without --local-only so the script can fetch the live homepage.",
            file=sys.stderr,
        )
        return 2

    print(f"Found {len(urls)} unique weapon image URLs")
    downloaded = download_urls(urls, output_dir, delay_seconds=max(args.delay, 0.0), skip_existing=not args.overwrite)
    print(f"Downloaded {downloaded} file(s) to {output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
