from bs4 import BeautifulSoup
import json

# Path to your saved HTML file
HTML_FILE = "c:\\temp\\Final_Fantasy_VII_Ever_Crisis_uw.html"

with open(HTML_FILE, "r", encoding="utf-8") as f:
    soup = BeautifulSoup(f, "html.parser")

weapons_data = []

# Iterate through headers that precede weapon tables
for header in soup.find_all(["h3"]):
    next_table = header.find_next("table")
    if not next_table:
        continue

    #if ("Cloud's broadswords" not in char_name
    #    or "Barret's gun-arms" not in char_name
    #    or "Tifa's knuckles" not in char_name
    #    or "Aerith's staves" not in char_name 
    #    or "Red XIII's collars" not in char_name 
    #    or "Yuffie's shuriken" not in char_name 
    #    or "Cait Sith's megaphones" not in char_name 
    #    or "Vincent's guns" not in char_name 
    #    or "Cid's spears" not in char_name 
    #    or "Zack's broadswords" not in char_name 
    #    or "Sephiroth's katana" not in char_name 
    #    or "Glenn's blades" not in char_name 
    #    or "Matt's bayonets" not in char_name 
    #    or "Lucia's rifles" not in char_name 
    #    or "Angeal's broadswords" not in char_name 
    #    or "Sephiroth (Original)'s katana" not in char_name):
    #    continue

    # Extract column headers
    table_header = next_table.find("thead")
    header_cells = table_header.find_all("th")
    headers = [th.get_text(strip=True) for th in header_cells]

    if "Ultimate Weapon" not in headers or "Reinforcement Abilities" not in headers:
        continue

    heading = next_table.find_previous_sibling("h3")
    char_heading = heading.get_text(strip=True).replace("[", "").replace("]", "")
    char_heading_parts = [a.strip() for a in char_heading.split("'") if a.strip()]
    char_name = char_heading_parts[0] if len(char_heading_parts) > 0 else None

    weapon_index = headers.index("Ultimate Weapon")
    reinforcement_index = headers.index("Reinforcement Abilities")

    table_body = next_table.find("tbody")

    print(table_body)

    # Process rows
    for row in table_body.find_all("tr"):
        cols = row.find_all(["th","td"])
        
        if len(cols) <= max(weapon_index, reinforcement_index):
            continue

        weapon_name = cols[weapon_index].get_text(strip=True)
        reinforcement_text = cols[reinforcement_index].get_text("\n", strip=True)
        abilities = [a.strip() for a in reinforcement_text.split("\n") if a.strip()]

        ability1 = abilities[0] if len(abilities) > 0 else None
        ability2 = abilities[1] if len(abilities) > 1 else None

        weapons_data.append({
            "character": char_name,
            "weapon": weapon_name,
            "ability1": ability1,
            "ability2": ability2
        })

# Save JSON
with open("c:\\temp\\ever_crisis_uweapons.json", "w", encoding="utf-8") as f:
    json.dump(weapons_data, f, ensure_ascii=False, indent=2)

print(f"Generated ever_crisis_uweapons.json with {len(weapons_data)} entries.")