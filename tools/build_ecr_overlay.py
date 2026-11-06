r"""Build the ECR Rebalancing overlay for the Gear Search (data/ecr/ecr-overlay.json).

Applies every change in the ECR launcher's recipe (C:\FF7EC\ECR\engine\data\masterdata-patches.json,
"ecr_rebalancing" section, all groups on, default choices) to the UnknownX7 MasterData JSON, using the
launcher's own engine code, and writes only the rows it changes or adds.

The recipe edits server tables by column index; the UnknownX7 JSON lists each row's fields in the same
column order, so column i is the row's i-th property.

Run with the launcher's bundled Python (it has the engine's dependencies):
    C:\FF7EC\ECR\python\python.exe -I tools\build_ecr_overlay.py [ECR root] [MasterData dir] [output]
The site runs this itself when the recipe is newer than the overlay (Services/EcrOverlayBuilder.cs).
"""
import copy
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
ecr_root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r'C:\FF7EC\ECR')
master_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else HERE / 'external/UnknownX7/FF7EC-Data/MasterData/gl'
output = Path(sys.argv[3]) if len(sys.argv) > 3 else HERE / 'data/ecr/ecr-overlay.json'

sys.path.insert(0, str(ecr_root))
from engine import masterdata_patches as mp  # noqa: E402

recipe_path = ecr_root / 'engine/data/masterdata-patches.json'
recipe_bytes = recipe_path.read_bytes()
recipe = json.loads(recipe_bytes.decode('utf-8-sig'))
section = recipe['ecr_rebalancing']
work = {'tables': {}, 'ecr_rebalancing': copy.deepcopy(section)}
mp._rebalance(work, True, {}, set())


def file_name(table):
    return ''.join(part.capitalize() for part in table[2:].split('_'))


tables, fields, keys = {}, {}, {}


def load(table):
    if table in tables:
        return
    path = master_dir / f'{file_name(table)}.json'
    rows = json.loads(path.read_text(encoding='utf-8')) if path.is_file() else []
    fields[table] = list(rows[0].keys()) if rows else None
    tables[table] = [list(row.values()) for row in rows]


for table in set(work['tables']) | set(work.get('added_rows', {})) | {
        'm_weapon', 'm_weapon_evolve', 'm_weapon_evolve_effect', 'm_weapon_evolve_weapon_skill', 'm_skill_weapon',
        'm_skill_active', 'm_skill_base', 'm_skill_base_level', 'm_skill_effect_group', 'm_skill_effect',
        'm_weapon_rarity', 'm_weapon_upgrade_skill', 'm_skill_trigger_condition_hp',
        'm_skill_trigger_condition_status_count'} | set(mp.CUSTOMIZATION_DETAIL.values()):
    load(table)

warnings, changed = [], {}
for table, spec in work['tables'].items():
    columns = spec['key_columns']
    keys[table] = columns
    lookup = {tuple(row[c] for c in columns): row for row in tables[table]}
    for key, column, originals, replacement in spec['cells']:
        row = lookup.get(tuple(key))
        if row is None or column >= len(row):
            warnings.append(f'{table} {key}: row or column {column} missing')
            continue
        if row[column] != replacement and row[column] not in originals:
            warnings.append(f'{table} {key} col {column}: found {row[column]!r}, expected {originals!r}')
        row[column] = replacement
        changed.setdefault(table, {})[tuple(key)] = row

added = {}


def add(table, row):
    load(table)
    tables[table].append(row)
    added.setdefault(table, []).append(row)


for table, rows in work.get('added_rows', {}).items():
    for row in rows:
        add(table, row)
mp._add_customizations(tables, work, lambda table: tables[table], add)


def as_object(table, row):
    names = fields.get(table)
    if not names:
        raise SystemExit(f'{table}: no UnknownX7 file to take field names from')
    if len(names) != len(row):
        warnings.append(f'{table}: row {row[:2]} has {len(row)} columns, the JSON {len(names)}')
    return {name: value for name, value in zip(names, row)}


out_tables = {}
for table in sorted(set(changed) | set(added)):
    names = fields[table]
    entry = {}
    if table in changed:
        entry['keys'] = [names[c] for c in keys[table]]
        entry['replace'] = [as_object(table, row) for row in changed[table].values()]
    if table in added:
        entry['add'] = [as_object(table, row) for row in added[table]]
    out_tables[file_name(table)] = entry

# Which weapons each change touches, by the weapon name before the ":" in the change's name.
weapon_rows = json.loads((master_dir / 'Weapon.json').read_text(encoding='utf-8'))
language = json.loads((HERE / 'external/UnknownX7/FF7EC-Data/Localization/en.json').read_text(encoding='utf-8'))
by_name = {}
for weapon in weapon_rows:
    name = language.get(str(weapon['NameLanguageId']))
    if name:
        by_name.setdefault(name.strip().lower(), []).append(weapon['Id'])
for costume in json.loads((master_dir / 'CharacterCostume.json').read_text(encoding='utf-8')):
    name = language.get(str(costume['NameLanguageId']))
    if name:
        by_name.setdefault(name.strip().lower(), []).append(f"costume-{costume['Id']}")

groups = {group['id']: group['name'] for group in section['groups']}
changes = []
for change in section['changes']:
    # Gear Search item ids: the weapon id, or "costume-<id>" for an outfit.
    gear_ids = sorted({str(spec['weapon']) for spec in change.get('customizations', ())}
                      | {str(x) for x in by_name.get(change['name'].rsplit(':', 1)[0].split('(')[0].strip().lower(), ())})
    changes.append({'id': change['id'], 'group': groups.get(change['group'], change['group']),
                    'name': change['name'], 'text': change['text'], 'gearIds': gear_ids})

texts = {str(text_id): value for values in work.get('added_texts', {}).values() for text_id, value in values.items()}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({
    'generatedUtc': datetime.now(timezone.utc).isoformat(timespec='seconds'),
    'recipeSha256': hashlib.sha256(recipe_bytes).hexdigest(),
    'recipeModifiedUtc': datetime.fromtimestamp(os.path.getmtime(recipe_path), timezone.utc).isoformat(timespec='seconds'),
    'warnings': warnings,
    'changes': changes,
    'texts': texts,
    'tables': out_tables,
}, ensure_ascii=False, indent=1), encoding='utf-8')
print(f'{len(changes)} changes, {sum(len(t.get("replace", [])) for t in out_tables.values())} rows changed, '
      f'{sum(len(t.get("add", [])) for t in out_tables.values())} rows added, {len(warnings)} warnings -> {output}')
