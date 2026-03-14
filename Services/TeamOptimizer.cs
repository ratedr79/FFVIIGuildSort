using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public sealed class TeamOptimizer
    {
        private readonly WeaponCatalog _weaponCatalog;
        private readonly SummonCatalog _summonCatalog;
        private readonly EnemyAbilityCatalog _enemyAbilityCatalog;
        private readonly MemoriaCatalog _memoriaCatalog;
        private const int MaxDpsAllowed = 2;

        public TeamOptimizer(WeaponCatalog weaponCatalog, SummonCatalog summonCatalog, EnemyAbilityCatalog enemyAbilityCatalog, MemoriaCatalog memoriaCatalog)
        {
            _weaponCatalog = weaponCatalog;
            _summonCatalog = summonCatalog;
            _enemyAbilityCatalog = enemyAbilityCatalog;
            _memoriaCatalog = memoriaCatalog;
        }

        public BestTeamResult FindBestTeam(AccountRow account, BattleContext context)
        {
            // 1) Extract all owned weapons and assign them to their character via weaponData.tsv.
            var weaponsByCharacter = new Dictionary<string, List<WeaponOwnership>>(StringComparer.OrdinalIgnoreCase);
            var costumesByCharacter = new Dictionary<string, List<CostumeOwnership>>(StringComparer.OrdinalIgnoreCase);
            var missingCatalogItems = new List<MissingCatalogItemBreakdown>();
            var ownedSummons = new List<SummonOwnership>();
            var ownedEnemyAbilities = new List<EnemyAbilityOwnership>();
            var ownedMemoria = new List<MemoriaOwnership>();
            var ownedMateria = new List<MateriaOwnership>();

            foreach (var kvp in account.ItemResponsesByColumnName)
            {
                var columnName = kvp.Key;
                var rawValue = kvp.Value;

                if (_summonCatalog.TryGetSummon(columnName, out var summonDef))
                {
                    var lvl = ParseSummonLevel(rawValue);
                    if (lvl != null && lvl.Value > 0)
                    {
                        ownedSummons.Add(new SummonOwnership { SummonName = summonDef.Name, Level = lvl.Value });
                    }
                    continue;
                }

                if (_enemyAbilityCatalog.TryGetEnemyAbility(columnName, out var enemyAbilityDef))
                {
                    var lvl = ParseEnemyAbilityLevel(rawValue);
                    if (lvl != null && lvl.Value > 0)
                    {
                        ownedEnemyAbilities.Add(new EnemyAbilityOwnership { AbilityName = enemyAbilityDef.Name, Level = lvl.Value });
                    }
                    continue;
                }

                if (_memoriaCatalog.TryGetMemoria(columnName, out var memoriaDef))
                {
                    var lvl = ParseMemoriaLevel(rawValue);
                    if (lvl != null && lvl.Value > 0)
                    {
                        ownedMemoria.Add(new MemoriaOwnership { MemoriaName = memoriaDef.Name, Level = lvl.Value });
                    }
                    continue;
                }

                if (TryParseMateriaColumn(columnName, out var materiaName, out var tier))
                {
                    var count = ParseCountValue(rawValue);
                    if (count > 0)
                    {
                        ownedMateria.Add(new MateriaOwnership { MateriaName = materiaName, Tier = tier, Count = count });
                    }
                    continue;
                }

                // Costumes (outfits): value is typically Own / Do Not Own
                if (_weaponCatalog.TryGetCostume(columnName, out var costumeInfo))
                {
                    var isOwned = rawValue.Equals("Own", StringComparison.OrdinalIgnoreCase) ||
                                 rawValue.Equals("Owned", StringComparison.OrdinalIgnoreCase);
                    if (!isOwned)
                    {
                        continue;
                    }

                    var costumeOwnership = new CostumeOwnership
                    {
                        CostumeName = costumeInfo.Name,
                        CharacterName = costumeInfo.Character,
                        Ownership = OwnershipType.Owned
                    };

                    if (!costumesByCharacter.TryGetValue(costumeOwnership.CharacterName, out var costumeList))
                    {
                        costumeList = new List<CostumeOwnership>();
                        costumesByCharacter[costumeOwnership.CharacterName] = costumeList;
                    }

                    costumeList.Add(costumeOwnership);
                    continue;
                }

                if (!_weaponCatalog.TryGetWeapon(columnName, out var weaponInfo))
                {
                    var isOwnedOutfitLike = rawValue.Equals("Own", StringComparison.OrdinalIgnoreCase) ||
                                           rawValue.Equals("Owned", StringComparison.OrdinalIgnoreCase);
                    var maybeOb = ParseWeaponOverboost(rawValue);
                    if (isOwnedOutfitLike)
                    {
                        missingCatalogItems.Add(new MissingCatalogItemBreakdown
                        {
                            ColumnName = columnName,
                            RawValue = rawValue,
                            InferredKind = "Outfit?",
                            Notes = "Column value indicates ownership, but item was not found in weaponData.tsv as a Costume."
                        });
                    }
                    else if (maybeOb != null)
                    {
                        missingCatalogItems.Add(new MissingCatalogItemBreakdown
                        {
                            ColumnName = columnName,
                            RawValue = rawValue,
                            InferredKind = "Weapon?",
                            Notes = "Column value looks like an owned weapon (OB/5 Star), but item was not found in weaponData.tsv as a Weapon."
                        });
                    }

                    continue; // not a weapon
                }

                var ob = ParseWeaponOverboost(rawValue);
                if (ob == null)
                {
                    continue; // not owned / unusable
                }

                var ownership = new WeaponOwnership
                {
                    WeaponName = weaponInfo.Name,
                    CharacterName = weaponInfo.Character,
                    IsUltimate = weaponInfo.IsUltimate,
                    OverboostLevel = ob.Value
                };

                if (!weaponsByCharacter.TryGetValue(ownership.CharacterName, out var list))
                {
                    list = new List<WeaponOwnership>();
                    weaponsByCharacter[ownership.CharacterName] = list;
                }

                list.Add(ownership);
            }

            // 2) Score each character based on their top weapons (simple initial model).
            var characterScores = new List<(string Character, double Score)>();
            var characterBreakdowns = new Dictionary<string, CharacterScoreBreakdown>(StringComparer.OrdinalIgnoreCase);
            var allCharacters = new HashSet<string>(weaponsByCharacter.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var ch in costumesByCharacter.Keys)
            {
                allCharacters.Add(ch);
            }

            foreach (var character in allCharacters)
            {
                var weapons = weaponsByCharacter.TryGetValue(character, out var wlist) ? wlist : new List<WeaponOwnership>();
                var role = CharacterRoleRegistry.GetRoleOrDefault(character);

                var selectedOwnership = SelectTwoWeapons(character, role, weapons, context);

                var ultimateOwnership = SelectUltimateWeapon(character, weapons, context);
                WeaponScoreBreakdown? ultimateBreakdown = null;
                if (ultimateOwnership != null)
                {
                    if (_weaponCatalog.TryGetWeapon(ultimateOwnership.WeaponName, out _))
                    {
                        ultimateBreakdown = ScoreWeapon(ultimateOwnership, context, slot: "Ultimate");
                        ultimateBreakdown.Slot = "Ultimate";
                        ultimateBreakdown.SelectionReason = "Ultimate weapon";
                    }
                    else
                    {
                        ultimateBreakdown = new WeaponScoreBreakdown
                        {
                            WeaponName = ultimateOwnership.WeaponName,
                            Slot = "Ultimate",
                            IsUltimate = true,
                            OverboostLevel = ultimateOwnership.OverboostLevel ?? 0,
                            SelectionReason = "Ultimate weapon (not found in weaponData.tsv)"
                        };
                    }
                }
                else
                {
                    ultimateBreakdown = new WeaponScoreBreakdown
                    {
                        WeaponName = "(Not owned)",
                        Slot = "Ultimate",
                        IsUltimate = true,
                        OverboostLevel = 0,
                        SelectionReason = "Ultimate weapon not owned"
                    };
                }

                var considered = weapons
                    .Select(w => ScoreWeapon(w, context))
                    .OrderByDescending(w => w.FinalWeaponScore)
                    .ToList();

                var selected = new List<WeaponScoreBreakdown>();
                if (selectedOwnership.Count > 0)
                {
                    var main = ScoreWeapon(selectedOwnership[0], context, slot: "Main-hand");
                    main.Slot = "Main-hand";
                    main.SelectionReason = GetSelectionReason(character, role, selectedOwnership[0], context, isMainHand: true);
                    selected.Add(main);
                }

                if (selectedOwnership.Count > 1)
                {
                    var off = ScoreWeapon(selectedOwnership[1], context, slot: "Off-hand");
                    off.Slot = "Off-hand";
                    off.SelectionReason = GetSelectionReason(character, role, selectedOwnership[1], context, isMainHand: false);
                    selected.Add(off);
                }

                var weaponScore = selected.Sum(w => w.FinalWeaponScore);
                var costumeBreakdowns = SelectAndScoreCostumes(character, costumesByCharacter, context);
                var costumeScore = costumeBreakdowns.Sum(c => c.FinalCostumeScore);
                var roleWeight = GetRoleWeight(role);
                var basePlusGear = weaponScore + costumeScore;

                characterBreakdowns[character] = new CharacterScoreBreakdown
                {
                    CharacterName = character,
                    Role = role,
                    RoleWeight = roleWeight,
                    ConsideredWeapons = considered,
                    SelectedWeapons = selected,
                    UltimateWeapon = ultimateBreakdown,
                    SelectedCostumes = costumeBreakdowns,
                    CostumeScoreSum = costumeScore,
                    RawWeaponScoreSum = weaponScore,
                    BasePlusGearScore = basePlusGear,
                    FinalCharacterScore = basePlusGear * roleWeight
                };

                characterScores.Add((character, basePlusGear * roleWeight));
            }

            // 3) Brute force best 3-character team using these character scores + constraint.
            var chars = characterScores
                .OrderByDescending(c => c.Score)
                .Select(c => c.Character)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedMemoria = SelectBestMemoria(ownedMemoria);
            var memoriaScore = selectedMemoria?.Score ?? 0;

            var materiaBreakdown = ScoreMateria(ownedMateria);
            var materiaScore = materiaBreakdown.Sum(m => m.Score);

            var selectedUtilityItems = SelectTopUtilityItems(ownedSummons, ownedEnemyAbilities, context);
            var utilityScore = selectedUtilityItems.Sum(u => u.Score);

            BestTeamResult best = new BestTeamResult { InGameName = account.InGameName, Score = double.MinValue };
            var altCandidates = new List<AlternateTeamResult>();

            for (int i = 0; i < chars.Count; i++)
            {
                for (int j = i + 1; j < chars.Count; j++)
                {
                    for (int k = j + 1; k < chars.Count; k++)
                    {
                        var team = new[] { chars[i], chars[j], chars[k] };
                        if (!IsValidTeam(team, context))
                        {
                            continue;
                        }

                        // Team score = sum of character scores + synergy bonus for the non-DPS slot.
                        var baseScore = team.Sum(ch => characterScores.First(x => x.Character.Equals(ch, StringComparison.OrdinalIgnoreCase)).Score);
                        var synergyBonus = CalculateSupportSynergyBonus(team, weaponsByCharacter, context);
                        var score = baseScore + synergyBonus + utilityScore + memoriaScore + materiaScore;

                        // Track candidates for alternate teams (exclude the final best team later).
                        altCandidates.Add(new AlternateTeamResult
                        {
                            Characters = team.ToList(),
                            Score = Math.Round(score, 2)
                        });

                        if (score > best.Score)
                        {
                            var breakdown = new TeamScoreBreakdown
                            {
                                InGameName = account.InGameName,
                                TeamScore = Math.Round(score, 2),
                                EnemyWeakness = context.EnemyWeakness,
                                PreferredDamageType = context.PreferredDamageType,
                                MaxDpsAllowed = MaxDpsAllowed,
                                SelectedMemoria = selectedMemoria,
                                MemoriaScore = memoriaScore,
                                Materia = materiaBreakdown,
                                MateriaScore = materiaScore,
                                SelectedUtilityItems = selectedUtilityItems,
                                UtilityScore = utilityScore,
                                Characters = team
                                    .Where(ch => characterBreakdowns.ContainsKey(ch))
                                    .Select(ch => characterBreakdowns[ch])
                                    .ToList(),
                                MissingCatalogItems = missingCatalogItems
                                    .OrderBy(m => m.InferredKind)
                                    .ThenBy(m => m.ColumnName, StringComparer.OrdinalIgnoreCase)
                                    .ToList(),
                                AppliedRules = new List<string>
                                {
                                    "TeamSize=3",
                                    $"MaxDpsAllowed={MaxDpsAllowed}",
                                    "Constraint: Sephiroth and Sephiroth (Original) cannot be on same team",
                                    "Role weights applied (DPS > Tank/Support/Healer)",
                                    "Weapon scoring emphasizes OB1/OB6/OB10",
                                    "Synergy bonus favors non-DPS utility matching weakness or preferred damage type",
                                    "Utility items (Summons + Enemy Abilities) add up to top 3 bonuses",
                                    "Memoria adds up to +150 scaled by level (max 1 per team)",
                                    "Materia adds a small capped bonus based on tiered counts"
                                }
                            };

                            best = new BestTeamResult
                            {
                                InGameName = account.InGameName,
                                Score = Math.Round(score, 2),
                                Characters = team.ToList(),
                                WeaponsByCharacter = weaponsByCharacter,
                                Breakdown = breakdown
                            };
                        }
                    }
                }
            }

            if (best.Score != double.MinValue)
            {
                var bestKey = string.Join("|", best.Characters.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
                best.AlternateTeams = altCandidates
                    .Where(t => string.Join("|", t.Characters.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)) != bestKey)
                    .OrderByDescending(t => t.Score)
                    .Take(5)
                    .ToList();
            }

            if (best.Score == double.MinValue)
            {
                best.Score = 0;
            }

            return best;
        }

        private List<CostumeScoreBreakdown> SelectAndScoreCostumes(string characterName, Dictionary<string, List<CostumeOwnership>> costumesByCharacter, BattleContext context)
        {
            if (!costumesByCharacter.TryGetValue(characterName, out var owned) || owned.Count == 0)
            {
                return new List<CostumeScoreBreakdown>();
            }

            // When character has more than 3 outfits, check elemental ability matches
            var useElementalScoring = owned.Count > 3;

            var scored = owned
                .Select(o => ScoreCostume(o, context, useElementalScoring))
                .OrderByDescending(s => s.BasePoints + s.SynergyPoints + s.ElementalAbilityPoints)
                .ToList();

            var selected = scored.Take(3).ToList();
            if (selected.Count == 0)
            {
                return selected;
            }

            // Main outfit full value.
            selected[0].Slot = "Main";
            selected[0].SlotMultiplier = 1.0;
            selected[0].FinalCostumeScore = Math.Round((selected[0].BasePoints + selected[0].SynergyPoints + selected[0].ElementalAbilityPoints) * selected[0].SlotMultiplier, 2);

            // Sub outfits half value.
            for (int i = 1; i < selected.Count; i++)
            {
                selected[i].Slot = "Sub";
                selected[i].SlotMultiplier = 0.5;
                selected[i].FinalCostumeScore = Math.Round((selected[i].BasePoints + selected[i].SynergyPoints + selected[i].ElementalAbilityPoints) * selected[i].SlotMultiplier, 2);
            }

            return selected;
        }

        private CostumeScoreBreakdown ScoreCostume(CostumeOwnership ownership, BattleContext context, bool checkElementalAbilities = false)
        {
            if (!_weaponCatalog.TryGetCostume(ownership.CostumeName, out var info))
            {
                return new CostumeScoreBreakdown { CostumeName = ownership.CostumeName, BasePoints = 100 };
            }

            var matchCount = SynergyDetection.CountSynergyMatches(info.EffectTextBlob, context);
            var synergyPoints = matchCount * 50;

            var elementalAbilityPoints = 0.0;
            if (checkElementalAbilities && context.EnemyWeakness != Element.None)
            {
                elementalAbilityPoints = ScoreElementalAbilities(info, context.EnemyWeakness);
            }

            return new CostumeScoreBreakdown
            {
                CostumeName = info.Name,
                Slot = string.Empty,
                BasePoints = 100,
                SynergyMatchCount = matchCount,
                SynergyPoints = synergyPoints,
                ElementalAbilityPoints = elementalAbilityPoints,
                SynergyReason = matchCount > 0 ? SynergyDetection.DescribeSynergyMatches(info.EffectTextBlob, context) : null,
                FinalCostumeScore = 0
            };
        }

        private double ScoreElementalAbilities(CostumeInfo costume, Element weakness)
        {
            if (costume.AdditionalAbilities == null || costume.AdditionalAbilities.Count == 0)
            {
                return 0;
            }

            var elementName = weakness.ToString();
            double score = 0;

            foreach (var ability in costume.AdditionalAbilities)
            {
                if (string.IsNullOrWhiteSpace(ability))
                {
                    continue;
                }

                var abilityText = ability.Trim();

                // Check for multi-element patterns like "Fire/Ice/Lightning/Earth/Water/Wind Ability Dmg. +30%"
                if (abilityText.Contains("Ability Dmg", StringComparison.OrdinalIgnoreCase))
                {
                    // Split on '/' to get individual elements
                    var parts = abilityText.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    var hasMatchingElement = false;

                    foreach (var part in parts)
                    {
                        if (part.Contains(elementName, StringComparison.OrdinalIgnoreCase))
                        {
                            hasMatchingElement = true;
                            break;
                        }
                    }

                    if (hasMatchingElement)
                    {
                        // Parse the percentage if present
                        var percentMatch = System.Text.RegularExpressions.Regex.Match(abilityText, @"\+(\d+)%");
                        if (percentMatch.Success && int.TryParse(percentMatch.Groups[1].Value, out var pct))
                        {
                            // Award points based on percentage: 30% = 30 points, etc.
                            score += pct;
                        }
                        else
                        {
                            // Default bonus if no percentage found
                            score += 25;
                        }
                    }
                }

                // Check for single-element patterns like "Fire Ability Dmg. +30%"
                else if (abilityText.Contains(elementName, StringComparison.OrdinalIgnoreCase) &&
                         abilityText.Contains("Ability Dmg", StringComparison.OrdinalIgnoreCase))
                {
                    var percentMatch = System.Text.RegularExpressions.Regex.Match(abilityText, @"\+(\d+)%");
                    if (percentMatch.Success && int.TryParse(percentMatch.Groups[1].Value, out var pct))
                    {
                        score += pct;
                    }
                    else
                    {
                        score += 25;
                    }
                }
            }

            return score;
        }

        private static bool IsValidTeam(IEnumerable<string> characters, BattleContext context)
        {
            var set = new HashSet<string>(characters, StringComparer.OrdinalIgnoreCase);

            if (set.Contains("Sephiroth") && set.Contains("Sephiroth (Original)"))
            {
                return false;
            }

            // At most two DPS.
            var dpsCount = set.Count(ch => CharacterRoleRegistry.GetRoleOrDefault(ch) == CharacterRole.DPS);
            if (dpsCount > MaxDpsAllowed)
            {
                return false;
            }

            // Must include at least one DPS.
            if (dpsCount < 1)
            {
                return false;
            }

            return true;
        }

        public static double GetRoleWeight(CharacterRole role)
        {
            return role switch
            {
                CharacterRole.DPS => 1.0,
                CharacterRole.Tank => 0.65,
                CharacterRole.Support => 0.6,
                CharacterRole.Healer => 0.55,
                _ => 1.0
            };
        }

        private int? ParseWeaponOverboost(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            raw = raw.Trim();

            if (raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Some survey exports use "Own" / "Owned" for weapons instead of 5 Star/OB.
            // Treat it as an owned weapon with OB0.
            if (raw.Equals("Own", StringComparison.OrdinalIgnoreCase) || raw.Equals("Owned", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (raw.Equals("5 Star", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (raw.StartsWith("OB", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw[2..], out var ob) && ob >= 0 && ob <= 10)
            {
                return ob;
            }

            // Some columns might be "Own" for non-weapon items; treat unknown values as not-owned for weapons.
            return null;
        }

        private bool TryParseMateriaColumn(string columnName, out string materiaName, out MateriaTier tier)
        {
            materiaName = string.Empty;
            tier = MateriaTier.Unknown;

            if (string.IsNullOrWhiteSpace(columnName))
            {
                return false;
            }

            // Example:
            // "Fira/Refined Fira Materia (11% Pot. or higher) Owned"
            // "Fira Blow/Refined Fira Blow Materia (8-10% Pot.) Owned"
            var ownedSuffix = " Owned";
            if (!columnName.EndsWith(ownedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var materiaIndex = columnName.IndexOf(" Materia (", StringComparison.OrdinalIgnoreCase);
            if (materiaIndex < 0)
            {
                return false;
            }

            var closeParenIndex = columnName.IndexOf(')', materiaIndex);
            if (closeParenIndex < 0)
            {
                return false;
            }

            materiaName = columnName.Substring(0, materiaIndex).Trim();
            var tierText = columnName.Substring(materiaIndex + " Materia (".Length, closeParenIndex - (materiaIndex + " Materia (".Length)).Trim();

            if (tierText.Contains("11%", StringComparison.OrdinalIgnoreCase) || tierText.Contains("or higher", StringComparison.OrdinalIgnoreCase))
            {
                tier = MateriaTier.Pot11Plus;
                return !string.IsNullOrWhiteSpace(materiaName);
            }

            if (tierText.Contains("8-10%", StringComparison.OrdinalIgnoreCase) || tierText.Contains("8–10%", StringComparison.OrdinalIgnoreCase))
            {
                tier = MateriaTier.Pot8To10;
                return !string.IsNullOrWhiteSpace(materiaName);
            }

            if (tierText.Contains("0-7%", StringComparison.OrdinalIgnoreCase) || tierText.Contains("0–7%", StringComparison.OrdinalIgnoreCase))
            {
                tier = MateriaTier.Pot0To7;
                return !string.IsNullOrWhiteSpace(materiaName);
            }

            return false;
        }

        private int ParseCountValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0;
            }

            raw = raw.Trim();

            if (raw.EndsWith("+", StringComparison.OrdinalIgnoreCase))
            {
                var num = raw[..^1];
                if (int.TryParse(num, out var plusVal))
                {
                    return Math.Max(0, plusVal);
                }
            }

            if (int.TryParse(raw, out var val))
            {
                return Math.Max(0, val);
            }

            return 0;
        }

        private List<MateriaScoreBreakdown> ScoreMateria(List<MateriaOwnership> ownedMateria)
        {
            if (ownedMateria.Count == 0)
            {
                return new List<MateriaScoreBreakdown>();
            }

            // Tiered buckets with separate caps. This keeps a player's materia contribution meaningful
            // without letting it dominate the overall score.
            const double cap11 = 90;
            const double cap8 = 60;
            const double cap0 = 30;

            // Weights chosen so that reaching each cap is plausible with a few owned materia,
            // but still requires meaningful ownership.
            const double w11 = 12;
            const double w8 = 8;
            const double w0 = 4;

            var c11Total = ownedMateria.Where(x => x.Tier == MateriaTier.Pot11Plus).Sum(x => x.Count);
            var c8Total = ownedMateria.Where(x => x.Tier == MateriaTier.Pot8To10).Sum(x => x.Count);
            var c0Total = ownedMateria.Where(x => x.Tier == MateriaTier.Pot0To7).Sum(x => x.Count);

            var score11 = Math.Min(cap11, c11Total * w11);
            var score8 = Math.Min(cap8, c8Total * w8);
            var score0 = Math.Min(cap0, c0Total * w0);

            var totalScore = Math.Round(score11 + score8 + score0, 2);

            return new List<MateriaScoreBreakdown>
            {
                new MateriaScoreBreakdown
                {
                    MateriaName = "Materia (All)",
                    CountPot11Plus = c11Total,
                    CountPot8To10 = c8Total,
                    CountPot0To7 = c0Total,
                    Score = totalScore
                }
            };
        }

        private int? ParseMemoriaLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            raw = raw.Trim();

            if (raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (raw.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
            {
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var lvlParsed))
                {
                    return lvlParsed;
                }
            }

            if (int.TryParse(raw, out var numericLevel))
            {
                return numericLevel;
            }

            return null;
        }

        private List<MemoriaScoreBreakdown> ScoreMemoria(List<MemoriaOwnership> ownedMemoria)
        {
            if (ownedMemoria.Count == 0)
            {
                return new List<MemoriaScoreBreakdown>();
            }

            var scored = new List<MemoriaScoreBreakdown>();
            foreach (var m in ownedMemoria)
            {
                if (!_memoriaCatalog.TryGetMemoria(m.MemoriaName, out var def))
                {
                    continue;
                }

                var maxLevel = Math.Max(1, def.MaxLevel);
                var clamped = Math.Max(0, Math.Min(m.Level, maxLevel));
                var frac = (double)clamped / maxLevel;
                var score = Math.Round(150.0 * frac, 2);

                scored.Add(new MemoriaScoreBreakdown
                {
                    MemoriaName = def.Name,
                    Level = clamped,
                    MaxLevel = maxLevel,
                    Score = score,
                    Abilities = def.Abilities ?? new List<string>()
                });
            }

            return scored;
        }

        private MemoriaScoreBreakdown? SelectBestMemoria(List<MemoriaOwnership> ownedMemoria)
        {
            var scored = ScoreMemoria(ownedMemoria);
            return scored
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.MemoriaName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private int? ParseSummonLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            raw = raw.Trim();

            if (raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (raw.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
            {
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var lvlParsed))
                {
                    return lvlParsed;
                }
            }

            if (int.TryParse(raw, out var numericLevel))
            {
                return numericLevel;
            }

            return null;
        }

        private List<SummonScoreBreakdown> ScoreSummons(List<SummonOwnership> ownedSummons, BattleContext context)
        {
            if (ownedSummons.Count == 0 || context.EnemyWeakness == Element.None)
            {
                return new List<SummonScoreBreakdown>();
            }

            var matching = new List<SummonScoreBreakdown>();
            foreach (var s in ownedSummons)
            {
                if (!_summonCatalog.TryGetSummon(s.SummonName, out var def))
                {
                    continue;
                }

                var weakness = context.EnemyWeakness.ToString();
                var elements = (def.Elements != null && def.Elements.Count > 0)
                    ? def.Elements
                    : new List<string> { def.Element };

                if (!elements.Any(e => e.Equals(weakness, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var maxLevel = Math.Max(1, def.MaxLevel);
                var clamped = Math.Max(0, Math.Min(s.Level, maxLevel));
                var frac = (double)clamped / maxLevel;
                var score = Math.Round(150.0 * frac, 2);

                matching.Add(new SummonScoreBreakdown
                {
                    SummonName = def.Name,
                    Level = clamped,
                    MaxLevel = maxLevel,
                    Score = score,
                    Reason = $"Matches weakness {context.EnemyWeakness} ({clamped}/{maxLevel})"
                });
            }

            return matching;
        }

        private int? ParseEnemyAbilityLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            raw = raw.Trim();

            if (raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (raw.StartsWith("Level", StringComparison.OrdinalIgnoreCase))
            {
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var lvlParsed))
                {
                    return lvlParsed;
                }
            }

            if (int.TryParse(raw, out var numericLevel))
            {
                return numericLevel;
            }

            return null;
        }

        private List<EnemyAbilityScoreBreakdown> ScoreEnemyAbilities(List<EnemyAbilityOwnership> ownedEnemyAbilities)
        {
            if (ownedEnemyAbilities.Count == 0)
            {
                return new List<EnemyAbilityScoreBreakdown>();
            }

            var scored = new List<EnemyAbilityScoreBreakdown>();
            foreach (var a in ownedEnemyAbilities)
            {
                if (!_enemyAbilityCatalog.TryGetEnemyAbility(a.AbilityName, out var def))
                {
                    continue;
                }

                var maxLevel = Math.Max(1, def.MaxLevel);
                var clamped = Math.Max(0, Math.Min(a.Level, maxLevel));
                var frac = (double)clamped / maxLevel;
                var score = Math.Round(def.MaxScore * frac, 2);

                scored.Add(new EnemyAbilityScoreBreakdown
                {
                    AbilityName = def.Name,
                    Level = clamped,
                    MaxLevel = maxLevel,
                    MaxScore = def.MaxScore,
                    Score = score,
                    Notes = def.Notes
                });
            }

            return scored;
        }

        private List<UtilityItemScoreBreakdown> SelectTopUtilityItems(List<SummonOwnership> ownedSummons, List<EnemyAbilityOwnership> ownedEnemyAbilities, BattleContext context)
        {
            var items = new List<UtilityItemScoreBreakdown>();

            foreach (var s in ScoreSummons(ownedSummons, context))
            {
                if (s.Score <= 0) continue;
                items.Add(new UtilityItemScoreBreakdown
                {
                    Kind = "Summon",
                    Name = s.SummonName,
                    Level = s.Level,
                    MaxLevel = s.MaxLevel,
                    Score = s.Score,
                    Notes = s.Reason
                });
            }

            foreach (var a in ScoreEnemyAbilities(ownedEnemyAbilities))
            {
                if (a.Score <= 0) continue;
                items.Add(new UtilityItemScoreBreakdown
                {
                    Kind = "EnemyAbility",
                    Name = a.AbilityName,
                    Level = a.Level,
                    MaxLevel = a.MaxLevel,
                    Score = a.Score,
                    Notes = a.Notes
                });
            }

            return items
                .OrderByDescending(i => i.Score)
                .ThenBy(i => i.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        public WeaponScoreBreakdown ScoreWeapon(WeaponOwnership w, BattleContext context, string? slot = null)
        {
            // Minimal viable weapon score model:
            // - Base score for being owned
            // - OB breakpoints matter: OB1 / OB6 / OB10 give bigger jumps
            // - Ultimate weapons are powerful but limited use; we discount them slightly.

            var ob = w.OverboostLevel ?? 0;
            var role = CharacterRoleRegistry.GetRoleOrDefault(w.CharacterName);

            var breakdown = new WeaponScoreBreakdown
            {
                WeaponName = w.WeaponName,
                Slot = slot,
                OverboostLevel = ob,
                IsUltimate = w.IsUltimate,
                BaseOwnedPoints = 100,
                Ob1Points = ob >= 1 ? 150 : 0,
                Ob6Points = ob >= 6 ? 250 : 0,
                Ob10Points = ob >= 10 ? 400 : 0,
                IntermediateObPoints = ob * 10,
                UltimateMultiplier = w.IsUltimate ? 0.85 : 1.0
            };

            // For DPS weapons, incorporate Ability Potency % (weaponData.tsv is OB10).
            // For non-DPS, damage contribution is less important; we down-weight it.
            double potencyContribution = 0;
            if (_weaponCatalog.TryGetWeapon(w.WeaponName, out var weaponInfo))
            {
                breakdown.SynergyReason = SynergyDetection.DescribeSynergy(weaponInfo, context);

                breakdown.AbilityPotPercentAtOb10 = weaponInfo.AbilityPotPercentAtOb10;

                breakdown.WeaknessMatch = context.EnemyWeakness == Element.None ||
                                         SynergyDetection.WeaponMatchesEnemyWeakness(weaponInfo, context.EnemyWeakness);

                breakdown.PreferredDamageTypeMatch = context.PreferredDamageType == DamageType.Any ||
                                                    SynergyDetection.WeaponMatchesPreferredDamageType(weaponInfo, context.PreferredDamageType);

                if (!w.IsUltimate && role == CharacterRole.DPS && weaponInfo.AbilityPotPercentAtOb10.HasValue)
                {
                    breakdown.PotencyApplied = true;
                    breakdown.AbilityPotPercentUsed = CalculateAbilityPotencyAtOb(weaponInfo.AbilityPotPercentAtOb10.Value, ob);
                    breakdown.MultiplyDamageBonusPercent = weaponInfo.MultiplyDamageBonusPercent;
                    breakdown.EffectiveAbilityPotPercentUsed = breakdown.AbilityPotPercentUsed.Value + breakdown.MultiplyDamageBonusPercent;

                    var isElemental = !string.IsNullOrWhiteSpace(weaponInfo.AbilityElement) &&
                                      !weaponInfo.AbilityElement.Equals("None", StringComparison.OrdinalIgnoreCase);

                    // If the off-hand doesn't match weakness, reduce potency weight (keep synergy unaffected).
                    var isOffHand = string.Equals(breakdown.Slot, "Off-hand", StringComparison.OrdinalIgnoreCase);
                    if (isOffHand && (context.EnemyWeakness != Element.None || context.PreferredDamageType != DamageType.Any))
                    {
                        var elementRelevant = context.EnemyWeakness != Element.None;
                        var typeRelevant = context.PreferredDamageType != DamageType.Any;

                        var elemOk = !elementRelevant || breakdown.WeaknessMatch;
                        var typeOk = !typeRelevant || breakdown.PreferredDamageTypeMatch;

                        // New rule:
                        // - If the weapon is elemental, its ability potency is only relevant when it matches the enemy weakness.
                        // - If it does NOT match weakness, potency is zero (regardless of Phys/Mag matching).
                        if (isElemental && elementRelevant && !breakdown.WeaknessMatch)
                        {
                            breakdown.PotencyWeightApplied = 0.0;
                        }
                        else if (isElemental && elementRelevant)
                        {
                            // Elemental + matches weakness: full potency (off-hand stays relevant).
                            breakdown.PotencyWeightApplied = 1.0;
                        }
                        else
                        {
                            // Non-elemental: use the existing element+type weighting behavior.

                            // Requested DPS off-hand potency weights:
                            // - matches element + type => 100%
                            // - matches element only => 50%
                            // - matches type only => 25%
                            // - matches neither => 0%
                            if (elemOk && typeOk)
                            {
                                breakdown.PotencyWeightApplied = 1.0;
                            }
                            else if (elemOk && !typeOk)
                            {
                                breakdown.PotencyWeightApplied = 0.50;
                            }
                            else if (!elemOk && typeOk)
                            {
                                breakdown.PotencyWeightApplied = 0.25;
                            }
                            else
                            {
                                breakdown.PotencyWeightApplied = 0.0;
                            }
                        }
                    }

                    if (isElemental && context.EnemyWeakness != Element.None && !breakdown.WeaknessMatch)
                    {
                        breakdown.PotencyWeightApplied = 0.0;
                    }

                    potencyContribution = breakdown.EffectiveAbilityPotPercentUsed.Value * breakdown.PotencyWeightApplied;
                }

                // For non-DPS, allow a weaker potency contribution when the weapon matches weakness.
                if (!w.IsUltimate && role != CharacterRole.DPS && weaponInfo.AbilityPotPercentAtOb10.HasValue && context.EnemyWeakness != Element.None && breakdown.WeaknessMatch)
                {
                    breakdown.PotencyApplied = true;
                    breakdown.AbilityPotPercentUsed = CalculateAbilityPotencyAtOb(weaponInfo.AbilityPotPercentAtOb10.Value, ob);
                    breakdown.MultiplyDamageBonusPercent = weaponInfo.MultiplyDamageBonusPercent;
                    breakdown.EffectiveAbilityPotPercentUsed = breakdown.AbilityPotPercentUsed.Value + breakdown.MultiplyDamageBonusPercent;
                    breakdown.PotencyWeightApplied = 0.50;
                    potencyContribution = breakdown.EffectiveAbilityPotPercentUsed.Value * breakdown.PotencyWeightApplied;
                }

                if (w.IsUltimate)
                {
                    breakdown.PotencyApplied = false;
                    breakdown.AbilityPotPercentUsed = null;
                    breakdown.EffectiveAbilityPotPercentUsed = null;
                    breakdown.MultiplyDamageBonusPercent = 0;
                    breakdown.PotencyWeightApplied = 1.0;
                }
            }

            var raw = breakdown.BaseOwnedPoints + breakdown.Ob1Points + breakdown.Ob6Points + breakdown.Ob10Points + breakdown.IntermediateObPoints;

            // Potency contribution is scaled down so it doesn't dwarf OB ownership scoring.
            raw += potencyContribution;

            // Non-DPS weapons: reduce raw damage weighting (utility comes via team synergy bonus instead).
            var nonDpsMultiplier = role == CharacterRole.DPS ? 1.0 : 0.55;

            var selfOnlyElementPenalty = 1.0;
            if (!w.IsUltimate && _weaponCatalog.TryGetWeapon(w.WeaponName, out var wi))
            {
                var isElemental = !string.IsNullOrWhiteSpace(wi.AbilityElement) &&
                                  !wi.AbilityElement.Equals("None", StringComparison.OrdinalIgnoreCase);

                var selfOnly = wi.AbilityRange.Equals("Self", StringComparison.OrdinalIgnoreCase) ||
                               (!string.IsNullOrWhiteSpace(wi.EffectTextBlob) && wi.EffectTextBlob.Contains("| Self", StringComparison.OrdinalIgnoreCase));

                if (isElemental && selfOnly)
                {
                    selfOnlyElementPenalty = 0.70;
                }

                if (isElemental && context.EnemyWeakness != Element.None &&
                    !SynergyDetection.WeaponMatchesEnemyWeakness(wi, context.EnemyWeakness))
                {
                    // Elemental weapon that doesn't match weakness shouldn't dominate purely on OB.
                    selfOnlyElementPenalty *= 0.70;
                }
            }

            breakdown.FinalWeaponScore = Math.Round(raw * breakdown.UltimateMultiplier * nonDpsMultiplier * selfOnlyElementPenalty, 2);

            return breakdown;
        }

        private static double CalculateAbilityPotencyAtOb(double ob10PotencyPercent, int overboostLevel)
        {
            // OB10 = baseline.
            // OB6 is usually 25% lower than OB10.
            // OB1 is usually 30% lower than OB6.
            // OB0 is usually 20% lower than OB1.
            if (overboostLevel >= 10)
            {
                return ob10PotencyPercent;
            }

            if (overboostLevel >= 6)
            {
                return ob10PotencyPercent * 0.75;
            }

            if (overboostLevel >= 1)
            {
                return ob10PotencyPercent * 0.75 * 0.70;
            }

            return ob10PotencyPercent * 0.75 * 0.70 * 0.80;
        }

        private List<WeaponOwnership> GetPreferredWeaponsForRole(List<WeaponOwnership> weapons, CharacterRole role, BattleContext context)
        {
            // For DPS: prefer weapons whose ability element matches enemy weakness and ability type matches preferred damage type.
            // For non-DPS: don't filter; utility comes from synergy scoring.
            if (role != CharacterRole.DPS)
            {
                return new List<WeaponOwnership>();
            }

            if (context.EnemyWeakness == Element.None && context.PreferredDamageType == DamageType.Any)
            {
                return new List<WeaponOwnership>();
            }

            var preferred = new List<WeaponOwnership>();
            foreach (var w in weapons)
            {
                if (!_weaponCatalog.TryGetWeapon(w.WeaponName, out var info))
                {
                    continue;
                }

                if (!IsAllowedForMainOrOffHand(info))
                {
                    continue;
                }

                if (!SynergyDetection.WeaponMatchesEnemyWeakness(info, context.EnemyWeakness))
                {
                    continue;
                }

                if (!SynergyDetection.WeaponMatchesPreferredDamageType(info, context.PreferredDamageType))
                {
                    continue;
                }

                preferred.Add(w);
            }

            return preferred;
        }

        private static bool IsAllowedForMainOrOffHand(WeaponInfo info)
        {
            // Ultimate weapons can ONLY appear in the Ultimate slot.
            if (info.IsUltimate || info.GachaType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Costumes are handled separately and should never be treated as weapons.
            if (info.GachaType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Allowed weapon sources for main/off-hand.
            // Keep unknown/empty as allowed for backwards compatibility / incomplete TSV rows.
            if (string.IsNullOrWhiteSpace(info.GachaType))
            {
                return true;
            }

            return info.GachaType.Equals("Featured", StringComparison.OrdinalIgnoreCase) ||
                   info.GachaType.Equals("Guild", StringComparison.OrdinalIgnoreCase) ||
                   info.GachaType.Equals("Crossover", StringComparison.OrdinalIgnoreCase) ||
                   info.GachaType.Equals("Limited", StringComparison.OrdinalIgnoreCase) ||
                   info.GachaType.Equals("Grindable", StringComparison.OrdinalIgnoreCase) ||
                   info.GachaType.Equals("Event", StringComparison.OrdinalIgnoreCase);
        }

        private List<WeaponOwnership> SelectTwoWeapons(string characterName, CharacterRole role, List<WeaponOwnership> weapons, BattleContext context)
        {
            var allowed = weapons
                .Where(w => _weaponCatalog.TryGetWeapon(w.WeaponName, out var info) && IsAllowedForMainOrOffHand(info))
                .ToList();

            if (allowed.Count == 0)
            {
                return new List<WeaponOwnership>();
            }

            if (allowed.Count == 1)
            {
                return new List<WeaponOwnership> { allowed[0] };
            }

            WeaponOwnership mainHand;

            if (role == CharacterRole.DPS)
            {
                // Main-hand (DPS): best potency weapon that matches weakness + preferred damage type (fallback to best potency overall).
                var matching = weapons
                    .Where(w => _weaponCatalog.TryGetWeapon(w.WeaponName, out var info) &&
                                SynergyDetection.WeaponMatchesEnemyWeakness(info, context.EnemyWeakness) &&
                                SynergyDetection.WeaponMatchesPreferredDamageType(info, context.PreferredDamageType))
                    .ToList();

                matching = matching
                    .Where(w => _weaponCatalog.TryGetWeapon(w.WeaponName, out var info) && IsAllowedForMainOrOffHand(info))
                    .ToList();

                var mainHandPool = matching.Count > 0 ? matching : allowed;
                mainHand = mainHandPool
                    .OrderByDescending(ScoreWeaponPotencyForDps)
                    .ThenByDescending(w => ScoreWeapon(w, context, slot: "Main-hand").FinalWeaponScore)
                    .First();
            }
            else
            {
                // Main-hand (non-DPS): prioritize utility; down-weight preferred damage type matching.
                // We still prefer weakness match when relevant, but allow strong utility to win.
                var bestProvidersForMainHand = FindBestSynergyProviders(allowed, context);

                var weaknessRelevant = context.EnemyWeakness != Element.None;
                var weaknessMatching = weaknessRelevant
                    ? allowed.Where(w => _weaponCatalog.TryGetWeapon(w.WeaponName, out var info) && SynergyDetection.WeaponMatchesEnemyWeakness(info, context.EnemyWeakness)).ToList()
                    : new List<WeaponOwnership>();

                var mainHandPool = weaknessMatching.Count > 0 ? weaknessMatching : allowed;
                mainHand = mainHandPool
                    .OrderByDescending(w => ScoreWeaponSynergyUtilityWithDedupe(w, context, bestProvidersForMainHand))
                    .ThenByDescending(w => ScoreWeapon(w, context, slot: "Main-hand").FinalWeaponScore)
                    .First();
            }

            // Off-hand: best remaining utility (synergy score first, then general weapon score).
            var remaining = allowed.Where(w => !w.WeaponName.Equals(mainHand.WeaponName, StringComparison.OrdinalIgnoreCase)).ToList();
            var bestProviders = FindBestSynergyProviders(remaining, context);
            var offHand = remaining
                .OrderByDescending(w => ScoreWeaponSynergyUtilityWithDedupe(w, context, bestProviders))
                .ThenByDescending(w => ScoreWeapon(w, context, slot: "Off-hand").FinalWeaponScore)
                .First();

            return new List<WeaponOwnership> { mainHand, offHand };
        }

        private sealed record SynergyProvider(string WeaponName, double Score, double CoverageWeight);

        private Dictionary<string, SynergyProvider> FindBestSynergyProviders(List<WeaponOwnership> weapons, BattleContext context)
        {
            // Option A: only consider a small set of high-impact effects for de-dupe.
            // Currently implemented: elemental resistance down (e.g., Fire Resistance Down).
            var best = new Dictionary<string, SynergyProvider>(StringComparer.OrdinalIgnoreCase);

            if (context.EnemyWeakness == Element.None)
            {
                return best;
            }

            foreach (var w in weapons)
            {
                if (!_weaponCatalog.TryGetWeapon(w.WeaponName, out var info))
                {
                    continue;
                }

                // Treat "Status Ailment: <Element> Weakness" as a different effect type.
                // We only consider "<Element> Resistance Down" for de-dupe here.
                var token = $"{context.EnemyWeakness} Resistance Down";
                if (string.IsNullOrWhiteSpace(info.EffectTextBlob) || !info.EffectTextBlob.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ob = w.OverboostLevel ?? 0;
                var score = SynergyDetection.CalculateSynergyScore(info, ob, context);
                var coverage = SynergyDetection.GetSynergyCoverageWeight(info, context);
                var key = $"elem_res_down:{context.EnemyWeakness}";

                if (!best.TryGetValue(key, out var existing))
                {
                    best[key] = new SynergyProvider(w.WeaponName, score, coverage);
                    continue;
                }

                // Prefer better coverage first, then higher synergy score.
                // Coverage should separate All > Single > Self.
                if (coverage > existing.CoverageWeight + 0.0001 ||
                    (Math.Abs(coverage - existing.CoverageWeight) < 0.0001 && score > existing.Score))
                {
                    best[key] = new SynergyProvider(w.WeaponName, score, coverage);
                }
            }

            return best;
        }

        private double ScoreWeaponSynergyUtilityWithDedupe(WeaponOwnership weapon, BattleContext context, Dictionary<string, SynergyProvider> bestProviders)
        {
            if (!_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info))
            {
                return 0;
            }

            var baseScore = SynergyDetection.CalculateSynergyScore(info, weapon.OverboostLevel ?? 0, context);

            if (context.EnemyWeakness != Element.None && !string.IsNullOrWhiteSpace(info.EffectTextBlob))
            {
                var token = $"{context.EnemyWeakness} Resistance Down";
                if (info.EffectTextBlob.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    var key = $"elem_res_down:{context.EnemyWeakness}";
                    if (bestProviders.TryGetValue(key, out var best) &&
                        !best.WeaponName.Equals(weapon.WeaponName, StringComparison.OrdinalIgnoreCase))
                    {
                        // If another weapon is the chosen provider for this effect, down-rank redundant versions.
                        // Further down-rank when this weapon has weaker coverage.
                        var coverage = SynergyDetection.GetSynergyCoverageWeight(info, context);
                        var coveragePenalty = coverage < best.CoverageWeight - 0.0001 ? 0.35 : 0.55;
                        baseScore *= coveragePenalty;
                    }
                }
            }

            return baseScore;
        }

        private WeaponOwnership? SelectUltimateWeapon(string characterName, List<WeaponOwnership> weapons, BattleContext context)
        {
            if (weapons.Count == 0)
            {
                return null;
            }

            // Determine ultimates by consulting weaponData.tsv (GachaType == Ultimate), not by the ownership flag.
            var ultimates = weapons
                .Where(w => _weaponCatalog.TryGetWeapon(w.WeaponName, out var info) && info.IsUltimate)
                .ToList();

            if (ultimates.Count == 0)
            {
                return null;
            }

            if (ultimates.Count == 1)
            {
                return ultimates[0];
            }

            // Multiple ultimates: score by synergy first, then by ability matching
            var scored = ultimates
                .Select(u => new
                {
                    Weapon = u,
                    SynergyScore = ScoreWeaponSynergyUtility(u, context),
                    AbilityScore = ScoreUltimateWeaponAbilities(u, context)
                })
                .OrderByDescending(x => x.SynergyScore)
                .ThenByDescending(x => x.AbilityScore)
                .ThenBy(x => x.Weapon.WeaponName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return scored[0].Weapon;
        }

        private double ScoreUltimateWeaponAbilities(WeaponOwnership weapon, BattleContext context)
        {
            if (!_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info))
            {
                return 0;
            }

            if (context.EnemyWeakness == Element.None)
            {
                return 0;
            }

            var elementName = context.EnemyWeakness.ToString();
            double score = 0;

            // Check AdditionalAbility1 and AdditionalAbility2 from additionalUltimateWeaponData.json
            var abilities = new[] { info.AdditionalAbility1, info.AdditionalAbility2 }
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!.Trim())
                .ToList();

            foreach (var ability in abilities)
            {
                // "Boost Elem. Pot. Arcanum" matches all elements
                if (ability.Contains("Boost Elem. Pot. Arcanum", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse the points value if present
                    var pointsMatch = System.Text.RegularExpressions.Regex.Match(ability, @"\+(\d+)\s*pts?");
                    if (pointsMatch.Success && int.TryParse(pointsMatch.Groups[1].Value, out var pts))
                    {
                        score += pts;
                    }
                    else
                    {
                        score += 30; // Default bonus
                    }
                }
                // Check for specific element mentions in abilities
                else if (ability.Contains(elementName, StringComparison.OrdinalIgnoreCase))
                {
                    var pointsMatch = System.Text.RegularExpressions.Regex.Match(ability, @"\+(\d+)\s*pts?");
                    if (pointsMatch.Success && int.TryParse(pointsMatch.Groups[1].Value, out var pts))
                    {
                        score += pts;
                    }
                    else
                    {
                        score += 20;
                    }
                }
            }

            return score;
        }

        private double ScoreWeaponSynergyUtility(WeaponOwnership weapon, BattleContext context)
        {
            if (_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info))
            {
                return SynergyDetection.CalculateSynergyScore(info, weapon.OverboostLevel ?? 0, context);
            }

            return 0;
        }

        private double ScoreWeaponPotencyForDps(WeaponOwnership weapon)
        {
            var ob = weapon.OverboostLevel ?? 0;
            if (_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info) && info.AbilityPotPercentAtOb10.HasValue)
            {
                return CalculateAbilityPotencyAtOb(info.AbilityPotPercentAtOb10.Value, ob) + info.MultiplyDamageBonusPercent;
            }

            return 0;
        }

        private string GetSelectionReason(string characterName, CharacterRole role, WeaponOwnership weapon, BattleContext context, bool isMainHand)
        {
            if (!_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info))
            {
                return role == CharacterRole.DPS && isMainHand ? "Main-hand damage weapon" : "Utility weapon";
            }

            if (role == CharacterRole.DPS)
            {
                if (isMainHand)
                {
                    var elemOk = SynergyDetection.WeaponMatchesEnemyWeakness(info, context.EnemyWeakness);
                    var typeOk = SynergyDetection.WeaponMatchesPreferredDamageType(info, context.PreferredDamageType);
                    if (context.EnemyWeakness != Element.None && context.PreferredDamageType != DamageType.Any)
                    {
                        return elemOk && typeOk
                            ? $"Main-hand DPS (matches {context.EnemyWeakness} + {context.PreferredDamageType})"
                            : "Main-hand DPS (best available potency; no strict match)";
                    }

                    if (context.EnemyWeakness != Element.None)
                    {
                        return elemOk
                            ? $"Main-hand DPS (matches {context.EnemyWeakness})"
                            : "Main-hand DPS (best available potency; no strict match)";
                    }

                    if (context.PreferredDamageType != DamageType.Any)
                    {
                        return typeOk
                            ? $"Main-hand DPS (matches {context.PreferredDamageType})"
                            : "Main-hand DPS (best available potency; no strict match)";
                    }

                    return "Main-hand DPS";
                }

                return "Off-hand utility weapon";
            }

            return "Utility weapon";
        }

        private double CalculateSupportSynergyBonus(IEnumerable<string> team, Dictionary<string, List<WeaponOwnership>> weaponsByCharacter, BattleContext context)
        {
            // Team-level synergy de-dupe:
            // - Build each character's selected main/off-hand weapons
            // - Pick the best provider per synergy category using coverage + synergy score
            // - Sum the best per category
            // - Allow ATB+N stacking (handled by summing base synergy score contributions that include ATB)

            var selectedWeapons = new List<WeaponOwnership>();
            foreach (var ch in team)
            {
                if (!weaponsByCharacter.TryGetValue(ch, out var owned) || owned.Count == 0)
                {
                    continue;
                }

                var role = CharacterRoleRegistry.GetRoleOrDefault(ch);
                var picked = SelectTwoWeapons(ch, role, owned, context);
                selectedWeapons.AddRange(picked);
            }

            if (selectedWeapons.Count == 0)
            {
                return 0;
            }

            var bestByCategory = new Dictionary<string, SynergyProvider>(StringComparer.OrdinalIgnoreCase);
            double stackingAtbBonus = 0;

            foreach (var w in selectedWeapons)
            {
                if (!_weaponCatalog.TryGetWeapon(w.WeaponName, out var info))
                {
                    continue;
                }

                var ob = w.OverboostLevel ?? 0;
                var synergyScore = SynergyDetection.CalculateSynergyScore(info, ob, context);
                var coverage = SynergyDetection.GetSynergyCoverageWeight(info, context);

                // Allow ATB+N stacking: if the weapon provides any ATB+N, keep the full synergy score contribution.
                // (CalculateSynergyScore includes ATB+N as part of the total.)
                if (!string.IsNullOrWhiteSpace(info.EffectTextBlob) && info.EffectTextBlob.Contains("ATB+", StringComparison.OrdinalIgnoreCase))
                {
                    stackingAtbBonus += synergyScore;
                    continue;
                }

                foreach (var category in GetSynergyCategories(info, context))
                {
                    if (!bestByCategory.TryGetValue(category, out var existing))
                    {
                        bestByCategory[category] = new SynergyProvider(w.WeaponName, synergyScore, coverage);
                        continue;
                    }

                    if (coverage > existing.CoverageWeight + 0.0001 ||
                        (Math.Abs(coverage - existing.CoverageWeight) < 0.0001 && synergyScore > existing.Score))
                    {
                        bestByCategory[category] = new SynergyProvider(w.WeaponName, synergyScore, coverage);
                    }
                }
            }

            var dedupedBonus = bestByCategory.Values.Sum(v => v.Score);
            return dedupedBonus + stackingAtbBonus;
        }

        private IEnumerable<string> GetSynergyCategories(WeaponInfo weapon, BattleContext context)
        {
            // This is an Option A+ list: only the most impactful categories.
            // NOTE: "Status Ailment: <Element> Weakness" is intentionally NOT treated as the same as "<Element> Resistance Down".
            var cats = new List<string>();

            if (context.EnemyWeakness != Element.None && !string.IsNullOrWhiteSpace(weapon.EffectTextBlob))
            {
                var element = context.EnemyWeakness.ToString();
                if (weapon.EffectTextBlob.Contains($"{element} Resistance Down", StringComparison.OrdinalIgnoreCase))
                    cats.Add($"elem_res_down:{element}");
                if (weapon.EffectTextBlob.Contains($"{element} Damage Bonus", StringComparison.OrdinalIgnoreCase))
                    cats.Add($"elem_dmg_bonus:{element}");
                if (weapon.EffectTextBlob.Contains($"{element} Damage Up", StringComparison.OrdinalIgnoreCase))
                    cats.Add($"elem_dmg_up:{element}");
                if (weapon.EffectTextBlob.Contains($"{element} Weapon Boost", StringComparison.OrdinalIgnoreCase))
                    cats.Add($"elem_weapon_boost:{element}");
            }

            if (context.PreferredDamageType != DamageType.Any && !string.IsNullOrWhiteSpace(weapon.EffectTextBlob))
            {
                if (context.PreferredDamageType == DamageType.Physical)
                {
                    if (weapon.EffectTextBlob.Contains("Phys. Weapon Boost", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_weapon_boost");
                    if (weapon.EffectTextBlob.Contains("Phys. Dmg. Rcvd. Up", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_rcvd_up");
                    if (weapon.EffectTextBlob.Contains("Single-Tgt. Phys. Dmg. Rcvd. Up", StringComparison.OrdinalIgnoreCase) ||
                        weapon.EffectTextBlob.Contains("Status Ailment: Single-Tgt. Phys. Dmg. Rcvd. Up", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_rcvd_up_single");
                    if (weapon.EffectTextBlob.Contains("Phys. Damage Bonus", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_dmg_bonus");
                    if (weapon.EffectTextBlob.Contains("PATK Up", StringComparison.OrdinalIgnoreCase)) cats.Add("patk_up");
                    if (weapon.EffectTextBlob.Contains("PDEF Down", StringComparison.OrdinalIgnoreCase)) cats.Add("pdef_down");
                    if (weapon.EffectTextBlob.Contains("Phys. ATB Conservation Effect", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_atb_conservation");
                }
                else if (context.PreferredDamageType == DamageType.Magical)
                {
                    if (weapon.EffectTextBlob.Contains("Mag. Weapon Boost", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_weapon_boost");
                    if (weapon.EffectTextBlob.Contains("Mag. Dmg. Rcvd. Up", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_rcvd_up");
                    if (weapon.EffectTextBlob.Contains("Single-Tgt. Mag. Dmg. Rcvd. Up", StringComparison.OrdinalIgnoreCase) ||
                        weapon.EffectTextBlob.Contains("Status Ailment: Single-Tgt. Mag. Dmg. Rcvd. Up", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_rcvd_up_single");
                    if (weapon.EffectTextBlob.Contains("Mag. Damage Bonus", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_dmg_bonus");
                    if (weapon.EffectTextBlob.Contains("MATK Up", StringComparison.OrdinalIgnoreCase)) cats.Add("matk_up");
                    if (weapon.EffectTextBlob.Contains("MDEF Down", StringComparison.OrdinalIgnoreCase)) cats.Add("mdef_down");
                    if (weapon.EffectTextBlob.Contains("Mag. ATB Conservation Effect", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_atb_conservation");
                }
            }

            if (!string.IsNullOrWhiteSpace(weapon.EffectTextBlob))
            {
                if (weapon.EffectTextBlob.Contains("Haste", StringComparison.OrdinalIgnoreCase)) cats.Add("haste");
                if (weapon.EffectTextBlob.Contains("Exploit Weakness", StringComparison.OrdinalIgnoreCase)) cats.Add("exploit_weakness");
                if (weapon.EffectTextBlob.Contains("Enfeeble", StringComparison.OrdinalIgnoreCase) ||
                    weapon.EffectTextBlob.Contains("Status Ailment: Enfeeble", StringComparison.OrdinalIgnoreCase)) cats.Add("enfeeble");
                if (weapon.EffectTextBlob.Contains("Applied Stats Debuff Tier Increased", StringComparison.OrdinalIgnoreCase)) cats.Add("applied_stats_debuff_tier_increased");
                if (weapon.EffectTextBlob.Contains("Applied Stats Buff Tier Increased", StringComparison.OrdinalIgnoreCase)) cats.Add("applied_stats_buff_tier_increased");
            }

            return cats;
        }
    }
}
