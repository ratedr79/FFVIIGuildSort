from bs4 import BeautifulSoup
import numpy as np
import json

# Path to your saved HTML file
HTML_FILE = "c:\\temp\\Final_Fantasy_VII_Ever_Crisis_outfits.html"

with open(HTML_FILE, "r", encoding="utf-8") as f:
    soup = BeautifulSoup(f, "html.parser")

weapons_data = []

# Iterate through headers that precede weapon tables
for header in soup.find_all(["h3"]):
    next_table = header.find_next("table")
    if not next_table:
        continue

    # Extract column headers
    table_header = next_table.find("tr");
    
    header_cells = table_header.find_all("th")
    headers = [th.get_text(strip=True) for th in header_cells]

    if "Name" not in headers or "Reinforcement Ability 1" not in headers or "Reinforcement Ability 2" not in headers:
        continue

    heading = next_table.parent.parent.find_previous_sibling("h3")
    char_name = heading.get_text(strip=True).replace("[", "").replace("]", "")

    outfit_name_index = headers.index("Name")
    command_ability_index = headers.index("Command Ability")
    ability1_index = headers.index("Reinforcement Ability 1")
    ability2_index = headers.index("Reinforcement Ability 2")

    table_body = next_table.find("tbody")

    # Process rows
    for row in table_body.find_all("tr")[1:]:
        cols = row.find_all("td")
        
        if len(cols) <= max(outfit_name_index, command_ability_index, ability1_index, ability2_index):
            continue

        outfit_name = cols[outfit_name_index].get_text(strip=True)
        command_ability = cols[command_ability_index].get_text(strip=True)
        ability1_text = cols[ability1_index].get_text(strip=True)
        ability2_column = cols[ability2_index]
        
        if "Extension+" not in ability2_column.get_text():        
            for br in ability2_column.find_all('br'):
                br.replace_with('\n')
            for br in ability2_column.find_all(';'):
                br.replace_with('\n')

        ability2_text = ability2_column.get_text()
        
        #if "(" in ability1_text and (";" in ability1_text or "\n" in ability1_text):
        #    start_index = ability1_text.find("(")
        #    end_index = ability1_text.rfind(")")
        #    ability1_data = ability1_text[start_index + 1 : end_index]
        #else:
        #    ability1_data = ability1_text
        
        ability1_data = ability1_text
        
        if "(" in ability2_text and "\n" in ability2_text and "Extension+" not in ability2_text:
            start_index = ability2_text.find("(")
            
            if start_index > 0 and "HP+" in ability2_text:
                start_index = ability2_text.find("(", start_index + 1)
            
            end_index = ability2_text.rfind(")")
            ability2_data = ability2_text[start_index + 1 : end_index]
        else:
            ability2_data = ability2_text
 
        if "\n" in ability1_data:
            ability1 = [a.strip() for a in ability1_data.split("\n") if a.strip()]
        else:
            ability1 = [a.strip() for a in ability1_data.split(";") if a.strip()]

        if "\n" in ability2_data:
            ability2 = [a.strip() for a in ability2_data.split("\n") if a.strip()]
        else:
            ability2 = [a.strip() for a in ability2_data.split(";") if a.strip()]

        abilities = np.concatenate((ability1, ability2))

        weapons_data.append({
            "character": char_name,
            "outfit": outfit_name,
            "command": command_ability,
            "ability1": abilities[0] if len(abilities) > 0 else None,
            "ability2": abilities[1] if len(abilities) > 1 else None,
            "ability3": abilities[2] if len(abilities) > 2 else None,
            "ability4": abilities[3] if len(abilities) > 3 else None,
            "ability5": abilities[4] if len(abilities) > 4 else None,
            "ability6": abilities[5] if len(abilities) > 5 else None,
            "ability7": abilities[6] if len(abilities) > 6 else None,
            "ability8": abilities[6] if len(abilities) > 7 else None,
            "ability9": abilities[8] if len(abilities) > 8 else None,
            "ability10": abilities[9] if len(abilities) > 9 else None
        })

# Save JSON
with open("c:\\temp\\ever_crisis_outfits.json", "w", encoding="utf-8") as f:
    json.dump(weapons_data, f, ensure_ascii=False, indent=2)

print(f"Generated ever_crisis_outfits.json with {len(weapons_data)} entries.")