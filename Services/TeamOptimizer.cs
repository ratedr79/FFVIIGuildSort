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
                            Notes = "Column value indicates ownership, but item was not found in the weapon catalog as a Costume."
                        });
                    }
                    else if (maybeOb != null)
                    {
                        missingCatalogItems.Add(new MissingCatalogItemBreakdown
                        {
                            ColumnName = columnName,
                            RawValue = rawValue,
                            InferredKind = "Weapon?",
                            Notes = "Column value looks like an owned weapon (OB/5 Star), but item was not found in the weapon catalog as a Weapon."
                        });
                    }

                    continue;
                }

                var ob = ParseWeaponOverboost(rawValue);
                if (ob == null)
                {
                    continue;
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
                            SelectionReason = "Ultimate weapon (not found in weapon catalog)"
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

            BestTeamResult best = new BestTeamResult { InGameName = account.InGameName, DiscordName = account.DiscordName, Score = double.MinValue };
            var altCandidates = new List<AlternateTeamResult>();

            var teamsToEvaluate = new List<string[]>();
            for (int i = 0; i < chars.Count; i++)
            {
                for (int j = i + 1; j < chars.Count; j++)
                {
                    for (int k = j + 1; k < chars.Count; k++)
                    {
                        var team = new[] { chars[i], chars[j], chars[k] };
                        if (IsValidTeam(team, context))
                        {
                            teamsToEvaluate.Add(team);
                        }
                    }
                }
            }

            var enabledTemplates = context.EnabledTeamTemplates ?? GetDefaultEnabledTemplates();
            var teamTemplateMatches = teamsToEvaluate
                .Select(team => new { Team = team, MatchesTemplate = DoesTeamMatchAnyTemplate(team, enabledTemplates) })
                .ToList();

            var anyTemplateMatched = teamTemplateMatches.Any(t => t.MatchesTemplate);

            foreach (var teamMatch in teamTemplateMatches)
            {
                var team = teamMatch.Team;
                var matchesTemplate = teamMatch.MatchesTemplate;

                var teamCharacterBreakdowns = new Dictionary<string, CharacterScoreBreakdown>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in characterBreakdowns)
                {
                    var original = kvp.Value;
                    var copy = new CharacterScoreBreakdown
                    {
                        CharacterName = original.CharacterName,
                        Role = original.Role,
                        RoleWeight = original.RoleWeight,
                        ConsideredWeapons = original.ConsideredWeapons.ToList(),
                        SelectedWeapons = original.SelectedWeapons.ToList(),
                        UltimateWeapon = original.UltimateWeapon,
                        SelectedCostumes = original.SelectedCostumes.ToList(),
                        CostumeScoreSum = original.CostumeScoreSum,
                        RawWeaponScoreSum = original.RawWeaponScoreSum,
                        BasePlusGearScore = original.BasePlusGearScore,
                        FinalCharacterScore = original.FinalCharacterScore
                    };
                    teamCharacterBreakdowns[kvp.Key] = copy;
                }

                var updatedCharacters = OptimizeNonDpsWeaponSelections(team, weaponsByCharacter, teamCharacterBreakdowns, context);

                var teamCharacterScores = characterScores.ToList();
                foreach (var ch in updatedCharacters)
                {
                    if (teamCharacterBreakdowns.TryGetValue(ch, out var breakdown))
                    {
                        var index = teamCharacterScores.FindIndex(x => x.Character.Equals(ch, StringComparison.OrdinalIgnoreCase));
                        if (index >= 0)
                        {
                            teamCharacterScores[index] = (ch, breakdown.FinalCharacterScore);
                        }
                    }
                }

                var baseScore = team.Sum(ch => teamCharacterScores.First(x => x.Character.Equals(ch, StringComparison.OrdinalIgnoreCase)).Score);
                var selectedTeamWeapons = team
                    .Where(ch => teamCharacterBreakdowns.ContainsKey(ch))
                    .SelectMany(ch => teamCharacterBreakdowns[ch].SelectedWeapons.Select(w => new WeaponOwnership
                    {
                        WeaponName = w.WeaponName,
                        CharacterName = ch,
                        IsUltimate = w.IsUltimate,
                        OverboostLevel = w.OverboostLevel
                    }))
                    .ToList();
                var synergyResult = CalculateSupportSynergyBonus(selectedTeamWeapons, context);
                var synergyBonus = synergyResult.Bonus;
                var score = baseScore + synergyBonus + utilityScore + memoriaScore + materiaScore;

                if (!matchesTemplate)
                {
                    score *= 0.5;
                }

                altCandidates.Add(new AlternateTeamResult
                {
                    Characters = team.ToList(),
                    Score = Math.Round(score, 2)
                });

                if (score > best.Score)
                {
                    var appliedRules = new List<string>
                    {
                        "TeamSize=3",
                        $"MaxDpsAllowed={MaxDpsAllowed}",
                        "Phase3 synergy: weighted category dedupe + diminishing returns",
                        context.EnemyWeakness == Element.None ? "EnemyWeakness=None" : $"EnemyWeakness={context.EnemyWeakness}",
                        context.PreferredDamageType == DamageType.Any ? "PreferredDamageType=Any" : $"PreferredDamageType={context.PreferredDamageType}",
                        context.TargetScenario == EnemyTargetScenario.Unknown ? "TargetScenario=SingleEnemy(default)" : $"TargetScenario={context.TargetScenario}",
                        $"EnabledTemplates={string.Join(",", enabledTemplates)}"
                    };

                    if (matchesTemplate)
                    {
                        var templateName = GetMatchingTemplateName(team, enabledTemplates);
                        appliedRules.Insert(0, $"Valid team template: {templateName}");
                    }
                    else
                    {
                        appliedRules.Insert(0, "Non-template team (50% penalty applied)");
                    }

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
                        SynergyBonus = Math.Round(synergyBonus, 2),
                        SynergyNotes = synergyResult.Notes,
                        Characters = team
                            .Where(ch => teamCharacterBreakdowns.ContainsKey(ch))
                            .Select(ch => teamCharacterBreakdowns[ch])
                            .ToList(),
                        MissingCatalogItems = missingCatalogItems
                            .OrderBy(m => m.InferredKind)
                            .ThenBy(m => m.ColumnName, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        AppliedRules = appliedRules
                    };

                    best = new BestTeamResult
                    {
                        InGameName = account.InGameName,
                        DiscordName = account.DiscordName,
                        Score = Math.Round(score, 2),
                        Characters = team.ToList(),
                        WeaponsByCharacter = weaponsByCharacter,
                        Breakdown = breakdown
                    };
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

            var scored = owned
                .Select(o => ScoreCostume(o, context))
                .ToList();

            if (scored.Count == 0)
            {
                return new List<CostumeScoreBreakdown>();
            }

            var main = scored
                .OrderByDescending(GetPrimaryCostumeValue)
                .First();

            main.Slot = "Main";
            main.SlotMultiplier = 1.0;
            main.CommandAbilityEnabled = true;
            main.FinalCostumeScore = CalculateFinalCostumeScore(main);

            var selected = new List<CostumeScoreBreakdown> { main };

            var subCostumes = scored
                .Where(s => !ReferenceEquals(s, main))
                .OrderByDescending(GetSubCostumeValue)
                .Take(2)
                .ToList();

            foreach (var sub in subCostumes)
            {
                sub.Slot = "Sub";
                sub.SlotMultiplier = 0.5;
                sub.CommandAbilityEnabled = false;
                sub.FinalCostumeScore = CalculateFinalCostumeScore(sub);
                selected.Add(sub);
            }

            return selected;
        }

        private CostumeScoreBreakdown ScoreCostume(CostumeOwnership ownership, BattleContext context)
        {
            if (!_weaponCatalog.TryGetCostume(ownership.CostumeName, out var info))
            {
                return new CostumeScoreBreakdown { CostumeName = ownership.CostumeName, BasePoints = 100 };
            }

            var role = CharacterRoleRegistry.GetRoleOrDefault(info.Character);
            var matchCount = SynergyDetection.CountSynergyMatches(info.EffectTextBlob, context, info.AbilityElement);
            var matchScore = SynergyDetection.CalculateSynergyMatchScore(info.EffectTextBlob, context, info.AbilityElement);
            var synergyPoints = Math.Round(matchScore * 30.0, 2);
            var passiveTexts = GetCostumePassiveAbilityTexts(info);
            var passivePoints = ScorePassiveAbilityTexts(passiveTexts, context, role, includePointValues: false, maxScore: 180, treatAsCostume: true);
            var commandPoints = ScoreCostumeCommand(info, context, role, matchScore, synergyPoints);
            var contextPoints = ScoreCostumeContext(info, context, role, passiveTexts, matchScore);
            var reliabilityPoints = ScoreCostumeReliability(info, context, role, passiveTexts, matchScore);

            return new CostumeScoreBreakdown
            {
                CostumeName = info.Name,
                Slot = string.Empty,
                BasePoints = 30,
                PassivePoints = Math.Round(passivePoints, 2),
                CommandPoints = Math.Round(commandPoints, 2),
                ContextPoints = Math.Round(contextPoints, 2),
                ReliabilityPoints = Math.Round(Math.Max(0, reliabilityPoints), 2),
                SynergyMatchCount = matchCount,
                SynergyPoints = synergyPoints,
                SynergyReason = matchCount > 0 ? SynergyDetection.DescribeWeightedSynergyMatches(info.EffectTextBlob, context) : null,
                FinalCostumeScore = 0
            };
        }

        private static double GetPrimaryCostumeValue(CostumeScoreBreakdown breakdown)
        {
            return breakdown.BasePoints + breakdown.PassivePoints + breakdown.CommandPoints + breakdown.ContextPoints + breakdown.ReliabilityPoints + breakdown.SynergyPoints;
        }

        private static double GetSubCostumeValue(CostumeScoreBreakdown breakdown)
        {
            return (breakdown.BasePoints + breakdown.PassivePoints) * 0.5;
        }

        private static double CalculateFinalCostumeScore(CostumeScoreBreakdown breakdown)
        {
            var passiveContribution = (breakdown.BasePoints + breakdown.PassivePoints) * breakdown.SlotMultiplier;
            var activeContribution = breakdown.CommandAbilityEnabled
                ? breakdown.CommandPoints + breakdown.ContextPoints + breakdown.ReliabilityPoints + breakdown.SynergyPoints
                : 0.0;

            return Math.Round(passiveContribution + activeContribution, 2);
        }

        private static List<string> GetDefaultEnabledTemplates()
        {
            return new List<string>
            {
                "DPS/Support/Healer",
                "DPS/Tank/Healer",
                "DPS/Support/Tank",
                "DPS/DPS/Healer"
            };
        }

        private static bool DoesTeamMatchAnyTemplate(string[] team, List<string> enabledTemplates)
        {
            var teamRoles = team.Select(ch => CharacterRoleRegistry.GetRoleOrDefault(ch).ToString()).OrderBy(r => r).ToList();
            var teamRoleKey = string.Join("/", teamRoles);

            foreach (var templateName in enabledTemplates)
            {
                var templateRoles = templateName.Split('/').OrderBy(r => r).ToList();
                var templateRoleKey = string.Join("/", templateRoles);

                if (teamRoleKey.Equals(templateRoleKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetMatchingTemplateName(string[] team, List<string> enabledTemplates)
        {
            var teamRoles = team.Select(ch => CharacterRoleRegistry.GetRoleOrDefault(ch).ToString()).OrderBy(r => r).ToList();
            var teamRoleKey = string.Join("/", teamRoles);

            foreach (var templateName in enabledTemplates)
            {
                var templateRoles = templateName.Split('/').OrderBy(r => r).ToList();
                var templateRoleKey = string.Join("/", templateRoles);

                if (teamRoleKey.Equals(templateRoleKey, StringComparison.OrdinalIgnoreCase))
                {
                    return templateName;
                }
            }

            return "Unknown";
        }

        private static bool IsValidTeam(IEnumerable<string> characters, BattleContext context)
        {
            var set = new HashSet<string>(characters, StringComparer.OrdinalIgnoreCase);

            if (set.Contains("Sephiroth") && set.Contains("Sephiroth (Original)"))
            {
                return false;
            }

            var dpsCount = set.Count(ch => CharacterRoleRegistry.GetRoleOrDefault(ch) == CharacterRole.DPS);
            if (dpsCount > MaxDpsAllowed)
            {
                return false;
            }

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

            const double cap11 = 90;
            const double cap8 = 60;
            const double cap0 = 30;

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

            if (!_weaponCatalog.TryGetWeapon(w.WeaponName, out var weaponInfo))
            {
                breakdown.FinalWeaponScore = Math.Round(breakdown.BaseOwnedPoints * breakdown.UltimateMultiplier, 2);
                return breakdown;
            }

            breakdown.GearSearchEnriched = weaponInfo.GearSearchEnriched;
            breakdown.HasCustomizations = weaponInfo.HasCustomizations;
            breakdown.CustomizationDescriptions = breakdown.HasCustomizations
                ? weaponInfo.CustomizationDescriptions
                : new List<string>();
            breakdown.AbilityPotPercentAtOb10 = weaponInfo.AbilityPotPercentAtOb10;

            breakdown.SynergyReason = SynergyDetection.DescribeSynergy(weaponInfo, context);

            breakdown.WeaknessMatch = context.EnemyWeakness == Element.None ||
                                     SynergyDetection.WeaponMatchesEnemyWeakness(weaponInfo, context.EnemyWeakness);

            breakdown.PreferredDamageTypeMatch = context.PreferredDamageType == DamageType.Any ||
                                                SynergyDetection.WeaponMatchesPreferredDamageType(weaponInfo, context.PreferredDamageType);

            var synergyScore = SynergyDetection.CalculateSynergyScore(weaponInfo, ob, context);
            breakdown.EffectScore = Math.Round(synergyScore * GetEffectScoreWeight(role, slot), 2);
            breakdown.PassiveScore = ScorePassiveAbilityTexts(GetWeaponPassiveAbilityTexts(weaponInfo), context, role, includePointValues: true, maxScore: role == CharacterRole.DPS ? 190 : 205, treatAsCostume: false);
            breakdown.CustomizationScore = ScoreCustomizationDescriptions(breakdown.CustomizationDescriptions, context, role);
            breakdown.ReliabilityScore = ScoreWeaponReliability(weaponInfo, context, role, breakdown, synergyScore);

            if (!w.IsUltimate && weaponInfo.AbilityPotPercentAtOb10.HasValue)
            {
                breakdown.PotencyApplied = true;
                // Prefer real pot% from GearSearch data when available
                if (weaponInfo.PotPercentByOb.TryGetValue(ob, out var realPot) && realPot > 0)
                {
                    breakdown.AbilityPotPercentUsed = realPot;
                }
                else
                {
                    breakdown.AbilityPotPercentUsed = CalculateAbilityPotencyAtOb(weaponInfo.AbilityPotPercentAtOb10.Value, ob);
                }
                breakdown.MultiplyDamageBonusPercent = weaponInfo.MultiplyDamageBonusPercent;
                breakdown.EffectiveAbilityPotPercentUsed = breakdown.AbilityPotPercentUsed.Value + breakdown.MultiplyDamageBonusPercent;

                var isElemental = !string.IsNullOrWhiteSpace(weaponInfo.AbilityElement) &&
                                  !weaponInfo.AbilityElement.Equals("None", StringComparison.OrdinalIgnoreCase);

                breakdown.PotencyWeightApplied = GetRoleAdjustedDamageWeight(role, slot, weaponInfo, breakdown, context, synergyScore, out var suppressionReason);
                breakdown.RoleAdjustedDamageWeight = breakdown.PotencyWeightApplied;
                breakdown.DamageSuppressionReason = suppressionReason;

                if (isElemental && context.EnemyWeakness != Element.None && !breakdown.WeaknessMatch)
                {
                    breakdown.PotencyWeightApplied = 0.0;
                    breakdown.RoleAdjustedDamageWeight = 0.0;
                    breakdown.DamageSuppressionReason = $"Elemental weapon does not match teased weakness {context.EnemyWeakness}; direct damage suppressed.";
                }

                var potencyContribution = breakdown.EffectiveAbilityPotPercentUsed.Value * breakdown.PotencyWeightApplied;
                breakdown.DamageScore = Math.Round(potencyContribution, 2);
                var subtotal = breakdown.BaseOwnedPoints + breakdown.Ob1Points + breakdown.Ob6Points + breakdown.Ob10Points + breakdown.IntermediateObPoints + breakdown.DamageScore + breakdown.EffectScore + breakdown.PassiveScore + breakdown.CustomizationScore + breakdown.ReliabilityScore;
                breakdown.FinalWeaponScore = Math.Round(subtotal * breakdown.UltimateMultiplier, 2);
            }
            else
            {
                breakdown.PotencyApplied = false;
                breakdown.AbilityPotPercentUsed = null;
                breakdown.EffectiveAbilityPotPercentUsed = null;
                breakdown.MultiplyDamageBonusPercent = 0;
                breakdown.PotencyWeightApplied = 0.0;
                breakdown.RoleAdjustedDamageWeight = 0.0;
                breakdown.DamageSuppressionReason = w.IsUltimate ? "Ultimate weapon damage potency not modeled directly in team score." : "Weapon has no direct potency data; score comes from ownership, utility, passives, and reliability.";
                var subtotal = breakdown.BaseOwnedPoints + breakdown.Ob1Points + breakdown.Ob6Points + breakdown.Ob10Points + breakdown.IntermediateObPoints + breakdown.EffectScore + breakdown.PassiveScore + breakdown.CustomizationScore + breakdown.ReliabilityScore;
                breakdown.FinalWeaponScore = Math.Round(subtotal * breakdown.UltimateMultiplier, 2);
            }

            return breakdown;
        }

        private static double CalculateAbilityPotencyAtOb(double ob10PotencyPercent, int overboostLevel)
        {
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

        private static List<string> GetWeaponPassiveAbilityTexts(WeaponInfo weaponInfo)
        {
            if (weaponInfo.GearSearchRAbilityDescriptions.Count > 0)
            {
                return weaponInfo.GearSearchRAbilityDescriptions;
            }

            return new[] { weaponInfo.AdditionalAbility1, weaponInfo.AdditionalAbility2, weaponInfo.AdditionalAbility3 }
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!.Trim())
                .ToList();
        }

        private static List<string> GetCostumePassiveAbilityTexts(CostumeInfo costumeInfo)
        {
            return costumeInfo.AdditionalAbilities
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .ToList();
        }

        private static double GetEffectScoreWeight(CharacterRole role, string? slot)
        {
            if (role == CharacterRole.DPS)
            {
                return string.Equals(slot, "Off-hand", StringComparison.OrdinalIgnoreCase) ? 0.65 : 0.45;
            }

            return role switch
            {
                CharacterRole.Healer => 1.0,
                CharacterRole.Support => 0.95,
                CharacterRole.Tank => 0.85,
                _ => 0.9
            };
        }

        private static double GetRoleAdjustedDamageWeight(
            CharacterRole role,
            string? slot,
            WeaponInfo weaponInfo,
            WeaponScoreBreakdown breakdown,
            BattleContext context,
            double synergyScore,
            out string? suppressionReason)
        {
            suppressionReason = null;

            var isElemental = !string.IsNullOrWhiteSpace(weaponInfo.AbilityElement) &&
                              !weaponInfo.AbilityElement.Equals("None", StringComparison.OrdinalIgnoreCase);
            var hasWeaknessTease = context.EnemyWeakness != Element.None;
            var hasTypeTease = context.PreferredDamageType != DamageType.Any;
            var isOffHand = string.Equals(slot, "Off-hand", StringComparison.OrdinalIgnoreCase);

            if (role == CharacterRole.DPS)
            {
                var weight = isOffHand ? 0.45 : 1.0;

                if (isOffHand)
                {
                    if (synergyScore <= 0)
                    {
                        weight = 0.15;
                        suppressionReason = "Off-hand without relevant utility is scored mostly for passive contribution.";
                    }
                    else if (breakdown.WeaknessMatch && breakdown.PreferredDamageTypeMatch)
                    {
                        weight = 0.60;
                    }
                    else if (breakdown.WeaknessMatch || breakdown.PreferredDamageTypeMatch)
                    {
                        weight = 0.45;
                        suppressionReason = "Off-hand direct damage reduced because it only partially matches the teased context.";
                    }
                    else
                    {
                        weight = 0.30;
                        suppressionReason = "Off-hand direct damage reduced because it does not match the teased battle context.";
                    }
                }

                if (isElemental && hasWeaknessTease && !breakdown.WeaknessMatch)
                {
                    suppressionReason = $"Elemental weapon does not match teased weakness {context.EnemyWeakness}; direct damage suppressed.";
                    return 0.0;
                }

                return weight;
            }

            if (!hasWeaknessTease && !hasTypeTease)
            {
                suppressionReason = "Non-DPS weapon direct damage de-emphasized because there is no teased context yet.";
                return 0.12;
            }

            if (hasWeaknessTease && !isElemental && !breakdown.WeaknessMatch)
            {
                suppressionReason = $"Non-elemental {role} weapon in {context.EnemyWeakness} tease; direct damage heavily suppressed in favor of utility.";
                if (breakdown.PreferredDamageTypeMatch && synergyScore > 0)
                {
                    return 0.08;
                }

                return synergyScore > 0 ? 0.05 : 0.02;
            }

            if (breakdown.WeaknessMatch && breakdown.PreferredDamageTypeMatch)
            {
                suppressionReason = "Non-DPS weapon direct damage partially credited because it matches the teased battle context.";
                return synergyScore > 0 ? 0.20 : 0.15;
            }

            if (breakdown.WeaknessMatch)
            {
                suppressionReason = "Non-DPS weapon direct damage limited because it is valued mainly for enabling damage.";
                return synergyScore > 0 ? 0.16 : 0.12;
            }

            if (breakdown.PreferredDamageTypeMatch)
            {
                suppressionReason = "Non-DPS weapon matches damage type but is still scored mostly for utility.";
                return synergyScore > 0 ? 0.10 : 0.06;
            }

            suppressionReason = "Non-DPS weapon direct damage suppressed because it does not match the teased context.";
            return synergyScore > 0 ? 0.05 : 0.02;
        }

        private static double ScorePassiveAbilityTexts(IEnumerable<string> abilityTexts, BattleContext context, CharacterRole role, bool includePointValues, double maxScore, bool treatAsCostume)
        {
            double total = 0;

            foreach (var raw in abilityTexts.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                var text = raw.Trim();
                var points = includePointValues ? TryExtractPointValue(text) ?? 18 : 18;
                var multiplier = GetPassiveAbilityMultiplier(text, context, role, treatAsCostume);
                total += points * multiplier;
            }

            return Math.Round(Math.Min(maxScore, total), 2);
        }

        private static double GetPassiveAbilityMultiplier(string text, BattleContext context, CharacterRole role, bool treatAsCostume)
        {
            var multiplier = treatAsCostume ? 0.95 : 0.80;

            if (context.EnemyWeakness != Element.None && text.Contains(context.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                multiplier += 0.45;
            }

            if (context.PreferredDamageType == DamageType.Physical && IsPhysicalText(text))
            {
                multiplier += 0.35;
            }

            if (context.PreferredDamageType == DamageType.Magical && IsMagicalText(text))
            {
                multiplier += 0.35;
            }

            if (text.Contains("Arcanum", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Mastery", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Elem. Pot", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Ability Pot.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Damage Up", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Damage Bonus", StringComparison.OrdinalIgnoreCase))
            {
                multiplier += 0.30;
            }

            if (role == CharacterRole.DPS &&
                (IsOffensiveText(text) || IsPhysicalText(text) || IsMagicalText(text)))
            {
                multiplier += 0.22;
            }

            if ((role == CharacterRole.Support || role == CharacterRole.Healer || role == CharacterRole.Tank) &&
                (IsDefensiveText(text) || IsSupportiveText(text)))
            {
                multiplier += 0.22;
            }

            if (text.Contains("All Allies", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("All Enemies", StringComparison.OrdinalIgnoreCase))
            {
                multiplier += 0.12;
            }

            if (text.Contains("Self", StringComparison.OrdinalIgnoreCase))
            {
                multiplier -= 0.08;
            }

            if (text.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Sigil", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Ruin", StringComparison.OrdinalIgnoreCase))
            {
                multiplier -= 0.18;
            }

            if (text.Contains("Limit Break", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Summon", StringComparison.OrdinalIgnoreCase))
            {
                multiplier -= 0.08;
            }

            return Math.Max(0.35, multiplier);
        }

        private static double ScoreCostumeCommand(CostumeInfo info, BattleContext context, CharacterRole role, double matchScore, double synergyPoints)
        {
            double score = 35;
            var blob = info.EffectTextBlob ?? string.Empty;

            score += Math.Min(70, synergyPoints * 0.65);

            if (context.EnemyWeakness != Element.None && info.AbilityElement.Equals(context.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
            }

            if (context.PreferredDamageType == DamageType.Physical && IsPhysicalText(info.AbilityType))
            {
                score += 25;
            }
            else if (context.PreferredDamageType == DamageType.Magical && IsMagicalText(info.AbilityType))
            {
                score += 25;
            }

            if (role == CharacterRole.DPS && (IsOffensiveText(blob) || IsOffensiveText(info.AbilityType)))
            {
                score += 18;
            }

            if ((role == CharacterRole.Support || role == CharacterRole.Healer || role == CharacterRole.Tank) &&
                (IsSupportiveText(blob) || IsDefensiveText(blob)))
            {
                score += 18;
            }

            if (blob.Contains("Haste", StringComparison.OrdinalIgnoreCase) ||
                blob.Contains("ATB", StringComparison.OrdinalIgnoreCase) ||
                blob.Contains("Exploit Weakness", StringComparison.OrdinalIgnoreCase))
            {
                score += 14;
            }

            if (blob.Contains("All Allies", StringComparison.OrdinalIgnoreCase) ||
                blob.Contains("All Enemies", StringComparison.OrdinalIgnoreCase) ||
                info.AbilityRange.Equals("All Allies", StringComparison.OrdinalIgnoreCase) ||
                info.AbilityRange.Equals("All Enemies", StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (blob.Contains("Self", StringComparison.OrdinalIgnoreCase) || info.AbilityRange.Equals("Self", StringComparison.OrdinalIgnoreCase))
            {
                score -= 6;
            }

            if (matchScore < 0.25 && string.IsNullOrWhiteSpace(blob))
            {
                score -= 10;
            }

            return Math.Round(Math.Max(0, Math.Min(220, score)), 2);
        }

        private static double ScoreCostumeContext(CostumeInfo info, BattleContext context, CharacterRole role, IEnumerable<string> passiveTexts, double matchScore)
        {
            double score = 0;
            var texts = passiveTexts.ToList();

            if (context.EnemyWeakness != Element.None)
            {
                if (info.AbilityElement.Equals(context.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    score += 30;
                }

                if (texts.Any(t => t.Contains(context.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    score += 20;
                }
            }

            if (context.PreferredDamageType == DamageType.Physical)
            {
                if (IsPhysicalText(info.AbilityType))
                {
                    score += 18;
                }

                if (texts.Any(IsPhysicalText))
                {
                    score += 14;
                }
            }
            else if (context.PreferredDamageType == DamageType.Magical)
            {
                if (IsMagicalText(info.AbilityType))
                {
                    score += 18;
                }

                if (texts.Any(IsMagicalText))
                {
                    score += 14;
                }
            }

            if (role == CharacterRole.DPS && (texts.Any(IsOffensiveText) || IsOffensiveText(info.EffectTextBlob)))
            {
                score += 12;
            }

            if ((role == CharacterRole.Support || role == CharacterRole.Healer || role == CharacterRole.Tank) &&
                (texts.Any(IsSupportiveText) || texts.Any(IsDefensiveText) || IsSupportiveText(info.EffectTextBlob)))
            {
                score += 12;
            }

            score += Math.Min(18, matchScore * 8.0);
            return Math.Round(score, 2);
        }

        private static double ScoreCostumeReliability(CostumeInfo info, BattleContext context, CharacterRole role, IEnumerable<string> passiveTexts, double matchScore)
        {
            double score = 22;
            var texts = passiveTexts.ToList();

            if (context.EnemyWeakness == Element.None && context.PreferredDamageType == DamageType.Any)
            {
                score += 12;
            }

            if (info.AbilityRange.Equals("All Allies", StringComparison.OrdinalIgnoreCase) ||
                info.AbilityRange.Equals("All Enemies", StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
            }
            else if (info.AbilityRange.Equals("Self", StringComparison.OrdinalIgnoreCase))
            {
                score -= 8;
            }

            if (texts.Any(t => t.Contains("HP", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("Heal", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("PDEF", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("MDEF", StringComparison.OrdinalIgnoreCase)))
            {
                score += 10;
            }

            if (texts.Any(t => t.Contains("Arcanum", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("Mastery", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("PATK", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("MATK", StringComparison.OrdinalIgnoreCase)))
            {
                score += 8;
            }

            if (matchScore > 0)
            {
                score += Math.Min(12, matchScore * 5.0);
            }

            if (texts.Any(t => t.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("Sigil", StringComparison.OrdinalIgnoreCase) ||
                               t.Contains("Ruin", StringComparison.OrdinalIgnoreCase)))
            {
                score -= 8;
            }

            if (role == CharacterRole.DPS && texts.All(t => !IsOffensiveText(t)) && !IsOffensiveText(info.EffectTextBlob))
            {
                score -= 4;
            }

            return Math.Round(Math.Max(0, score), 2);
        }

        private static bool IsPhysicalText(string text)
        {
            return text.Contains("Phys", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("PATK", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMagicalText(string text)
        {
            return text.Contains("Mag", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("MATK", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOffensiveText(string text)
        {
            return text.Contains("Damage", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Pot.", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("PATK", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("MATK", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Exploit Weakness", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Weapon Boost", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportiveText(string text)
        {
            return text.Contains("Buff", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Debuff", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Heal", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Haste", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("ATB", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Ally", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Enemy", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefensiveText(string text)
        {
            return text.Contains("HP", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("PDEF", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("MDEF", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Defense", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Resist", StringComparison.OrdinalIgnoreCase);
        }

        private static int? TryExtractPointValue(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\+(\d+)\s*pts?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
        }

        private static double ScoreCustomizationDescriptions(IEnumerable<string> customizationDescriptions, BattleContext context, CharacterRole role)
        {
            double total = 0;

            foreach (var raw in customizationDescriptions.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                var text = raw.Trim();
                var score = 0.0;

                var damageUpgrade = text.Contains("Damage", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("Potency", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("Attack", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("PATK", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains("MATK", StringComparison.OrdinalIgnoreCase);

                var supportUpgrade = text.Contains("Buff", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("Debuff", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("Effect", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("ATB", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("Heal", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("Enemy", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("Ally", StringComparison.OrdinalIgnoreCase);

                var survivabilityUpgrade = text.Contains("HP", StringComparison.OrdinalIgnoreCase) ||
                                           text.Contains("PDEF", StringComparison.OrdinalIgnoreCase) ||
                                           text.Contains("MDEF", StringComparison.OrdinalIgnoreCase) ||
                                           text.Contains("Defense", StringComparison.OrdinalIgnoreCase);

                var nicheUpgrade = text.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) ||
                                   text.Contains("Sigil", StringComparison.OrdinalIgnoreCase) ||
                                   text.Contains("Ruin", StringComparison.OrdinalIgnoreCase);

                if (damageUpgrade)
                {
                    score = Math.Max(score, role == CharacterRole.DPS ? 32 : 12);
                }

                if (supportUpgrade)
                {
                    score = Math.Max(score, role == CharacterRole.DPS ? 18 : 24);
                }

                if (survivabilityUpgrade)
                {
                    score = Math.Max(score, role == CharacterRole.Tank || role == CharacterRole.Healer ? 16 : 10);
                }

                if (nicheUpgrade)
                {
                    score = Math.Max(score, 8);
                    score *= 0.8;
                }

                if (context.EnemyWeakness != Element.None && text.Contains(context.EnemyWeakness.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                }

                if (context.PreferredDamageType == DamageType.Physical && text.Contains("Phys", StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                }
                else if (context.PreferredDamageType == DamageType.Magical && text.Contains("Mag", StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                }

                total += score;
            }

            return Math.Round(Math.Min(role == CharacterRole.DPS ? 95 : 85, total), 2);
        }

        private static double ScoreWeaponReliability(WeaponInfo weaponInfo, BattleContext context, CharacterRole role, WeaponScoreBreakdown breakdown, double synergyScore)
        {
            var score = 10.0;

            if (context.EnemyWeakness != Element.None && breakdown.WeaknessMatch)
            {
                score += 20;
            }

            if (context.PreferredDamageType != DamageType.Any && breakdown.PreferredDamageTypeMatch)
            {
                score += 15;
            }

            var coverage = SynergyDetection.GetSynergyCoverageWeight(weaponInfo, context);
            score += (coverage - 1.0) * 40.0;

            if (synergyScore > 0)
            {
                score += Math.Min(role == CharacterRole.DPS ? 22 : 32, synergyScore * (role == CharacterRole.DPS ? 0.10 : 0.16));
            }

            var isElemental = !string.IsNullOrWhiteSpace(weaponInfo.AbilityElement) &&
                              !weaponInfo.AbilityElement.Equals("None", StringComparison.OrdinalIgnoreCase);
            if (!isElemental && context.EnemyWeakness != Element.None && role != CharacterRole.DPS)
            {
                score -= 8;
            }

            return Math.Round(Math.Max(0, score), 2);
        }

        private List<WeaponOwnership> GetPreferredWeaponsForRole(List<WeaponOwnership> weapons, CharacterRole role, BattleContext context)
        {
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
            if (info.IsUltimate || info.GachaType.Equals("Ultimate", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (info.GachaType.Equals("Costume", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

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

            var remaining = allowed.Where(w => !w.WeaponName.Equals(mainHand.WeaponName, StringComparison.OrdinalIgnoreCase)).ToList();
            var bestProviders = FindBestSynergyProviders(remaining, context);
            var offHand = remaining
                .OrderByDescending(w => ScoreWeaponSynergyUtilityWithDedupe(w, context, bestProviders))
                .ThenByDescending(w => ScoreWeapon(w, context, slot: "Off-hand").FinalWeaponScore)
                .First();

            return new List<WeaponOwnership> { mainHand, offHand };
        }

        private sealed record SynergyProvider(string WeaponName, double CategoryValue, double CoverageWeight);

        private Dictionary<string, SynergyProvider> FindBestSynergyProviders(List<WeaponOwnership> weapons, BattleContext context)
        {
            var best = new Dictionary<string, SynergyProvider>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in weapons)
            {
                if (!_weaponCatalog.TryGetWeapon(w.WeaponName, out var info))
                {
                    continue;
                }

                var ob = w.OverboostLevel ?? 0;
                var coverage = SynergyDetection.GetSynergyCoverageWeight(info, context);

                foreach (var category in GetSynergyCategories(info, context))
                {
                    var categoryValue = GetWeaponCategoryContribution(info, ob, context, category);
                    if (categoryValue <= 0)
                    {
                        continue;
                    }

                    if (!best.TryGetValue(category, out var existing) ||
                        categoryValue > existing.CategoryValue + 0.01 ||
                        (Math.Abs(categoryValue - existing.CategoryValue) < 0.01 && coverage > existing.CoverageWeight + 0.0001))
                    {
                        best[category] = new SynergyProvider(w.WeaponName, categoryValue, coverage);
                    }
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

            var baseScore = ScoreWeaponUtilityProfile(weapon, context, slot: "Utility");

            var ob = weapon.OverboostLevel ?? 0;
            var overlapPenalty = 0.0;

            foreach (var category in GetSynergyCategories(info, context).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!bestProviders.TryGetValue(category, out var best) ||
                    best.WeaponName.Equals(weapon.WeaponName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var categoryValue = GetWeaponCategoryContribution(info, ob, context, category);
                if (categoryValue <= 0)
                {
                    continue;
                }

                var penaltyRate = categoryValue < best.CategoryValue * 0.75 ? 0.30 : 0.45;
                overlapPenalty += categoryValue * penaltyRate;
            }

            if (overlapPenalty <= 0)
            {
                return baseScore;
            }

            return Math.Max(baseScore * 0.55, baseScore - overlapPenalty);
        }

        private WeaponOwnership? SelectUltimateWeapon(string characterName, List<WeaponOwnership> weapons, BattleContext context)
        {
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

            var abilities = new[] { info.AdditionalAbility1, info.AdditionalAbility2 }
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!.Trim())
                .ToList();

            foreach (var ability in abilities)
            {
                if (ability.Contains("Boost Elem. Pot. Arcanum", StringComparison.OrdinalIgnoreCase))
                {
                    var pointsMatch = System.Text.RegularExpressions.Regex.Match(ability, @"\+(\d+)\s*pts?");
                    if (pointsMatch.Success && int.TryParse(pointsMatch.Groups[1].Value, out var pts))
                    {
                        score += pts;
                    }
                    else
                    {
                        score += 30;
                    }
                }
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
                var breakdown = ScoreWeapon(weapon, context, slot: "Utility");
                return breakdown.EffectScore + breakdown.PassiveScore + breakdown.CustomizationScore + breakdown.ReliabilityScore + (breakdown.DamageScore * 0.15);
            }

            return 0;
        }

        private double ScoreWeaponUtilityProfile(WeaponOwnership weapon, BattleContext context, string? slot = null)
        {
            var breakdown = ScoreWeapon(weapon, context, slot ?? "Utility");
            return breakdown.EffectScore + breakdown.PassiveScore + breakdown.CustomizationScore + breakdown.ReliabilityScore + (breakdown.DamageScore * 0.15);
        }

        private double ScoreWeaponPotencyForDps(WeaponOwnership weapon)
        {
            var ob = weapon.OverboostLevel ?? 0;
            if (_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info) && info.AbilityPotPercentAtOb10.HasValue)
            {
                double basePot;
                if (info.PotPercentByOb.TryGetValue(ob, out var realPot) && realPot > 0)
                {
                    basePot = realPot;
                }
                else
                {
                    basePot = CalculateAbilityPotencyAtOb(info.AbilityPotPercentAtOb10.Value, ob);
                }
                return basePot + info.MultiplyDamageBonusPercent;
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

        private (double Bonus, List<string> Notes) CalculateSupportSynergyBonus(IEnumerable<WeaponOwnership> selectedWeapons, BattleContext context)
        {
            var pickedWeapons = selectedWeapons.ToList();

            if (pickedWeapons.Count == 0)
            {
                return (0, new List<string>());
            }

            var bestByCategory = new Dictionary<string, SynergyProvider>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in pickedWeapons)
            {
                if (!_weaponCatalog.TryGetWeapon(w.WeaponName, out var info))
                {
                    continue;
                }

                var ob = w.OverboostLevel ?? 0;
                var coverage = SynergyDetection.GetSynergyCoverageWeight(info, context);

                foreach (var category in GetSynergyCategories(info, context).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var categoryValue = GetWeaponCategoryContribution(info, ob, context, category);
                    if (categoryValue <= 0)
                    {
                        continue;
                    }

                    if (!bestByCategory.TryGetValue(category, out var existing) ||
                        categoryValue > existing.CategoryValue + 0.01 ||
                        (Math.Abs(categoryValue - existing.CategoryValue) < 0.01 && coverage > existing.CoverageWeight + 0.0001))
                    {
                        bestByCategory[category] = new SynergyProvider(w.WeaponName, categoryValue, coverage);
                    }
                }
            }

            var rankedCategories = bestByCategory
                .Select(kvp => new
                {
                    Category = kvp.Key,
                    Provider = kvp.Value
                })
                .OrderByDescending(x => x.Provider.CategoryValue)
                .ToList();

            double total = 0;
            var notes = new List<string>();
            for (int i = 0; i < rankedCategories.Count; i++)
            {
                var multiplier = GetSynergyStackingMultiplier(i);
                var weightedValue = rankedCategories[i].Provider.CategoryValue * multiplier;
                total += weightedValue;
                notes.Add($"{rankedCategories[i].Provider.WeaponName}: {SynergyDetection.DescribeSynergyCategory(rankedCategories[i].Category)} = {rankedCategories[i].Provider.CategoryValue:N1} x {multiplier:N2} = {weightedValue:N1}");
            }

            return (Math.Round(total, 2), notes);
        }

        private IEnumerable<string> GetSynergyCategories(WeaponInfo weapon, BattleContext context)
        {
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
                    if (SynergyDetection.ProvidesDamageReceivedUp(weapon, DamageType.Physical, context)) cats.Add("phys_rcvd_up");
                    if (SynergyDetection.ProvidesSingleTargetDamageReceivedUp(weapon, DamageType.Physical, context)) cats.Add("phys_rcvd_up_single");
                    if (SynergyDetection.ProvidesAllTargetDamageReceivedUp(weapon, DamageType.Physical, context)) cats.Add("phys_rcvd_up_all");
                    if (weapon.EffectTextBlob.Contains("Phys. Damage Bonus", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_dmg_bonus");
                    if (weapon.EffectTextBlob.Contains("PATK Up", StringComparison.OrdinalIgnoreCase)) cats.Add("patk_up");
                    if (weapon.EffectTextBlob.Contains("PDEF Down", StringComparison.OrdinalIgnoreCase)) cats.Add("pdef_down");
                    if (weapon.EffectTextBlob.Contains("Phys. ATB Conservation Effect", StringComparison.OrdinalIgnoreCase)) cats.Add("phys_atb_conservation");
                }
                else if (context.PreferredDamageType == DamageType.Magical)
                {
                    if (weapon.EffectTextBlob.Contains("Mag. Weapon Boost", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_weapon_boost");
                    if (SynergyDetection.ProvidesDamageReceivedUp(weapon, DamageType.Magical, context)) cats.Add("mag_rcvd_up");
                    if (SynergyDetection.ProvidesSingleTargetDamageReceivedUp(weapon, DamageType.Magical, context)) cats.Add("mag_rcvd_up_single");
                    if (SynergyDetection.ProvidesAllTargetDamageReceivedUp(weapon, DamageType.Magical, context)) cats.Add("mag_rcvd_up_all");
                    if (weapon.EffectTextBlob.Contains("Mag. Damage Bonus", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_dmg_bonus");
                    if (weapon.EffectTextBlob.Contains("MATK Up", StringComparison.OrdinalIgnoreCase)) cats.Add("matk_up");
                    if (weapon.EffectTextBlob.Contains("MDEF Down", StringComparison.OrdinalIgnoreCase)) cats.Add("mdef_down");
                    if (weapon.EffectTextBlob.Contains("Mag. ATB Conservation Effect", StringComparison.OrdinalIgnoreCase)) cats.Add("mag_atb_conservation");
                }
            }

            if (!string.IsNullOrWhiteSpace(weapon.EffectTextBlob))
            {
                if (weapon.EffectTextBlob.Contains("ATB+", StringComparison.OrdinalIgnoreCase)) cats.Add("atb_plus");
                if (weapon.EffectTextBlob.Contains("Haste", StringComparison.OrdinalIgnoreCase)) cats.Add("haste");
                if (weapon.EffectTextBlob.Contains("Exploit Weakness", StringComparison.OrdinalIgnoreCase)) cats.Add("exploit_weakness");
                if (weapon.EffectTextBlob.Contains("Enfeeble", StringComparison.OrdinalIgnoreCase) ||
                    weapon.EffectTextBlob.Contains("Status Ailment: Enfeeble", StringComparison.OrdinalIgnoreCase)) cats.Add("enfeeble");
                if (weapon.EffectTextBlob.Contains("Enliven", StringComparison.OrdinalIgnoreCase) ||
                    weapon.EffectTextBlob.Contains("Status Ailment: Enliven", StringComparison.OrdinalIgnoreCase)) cats.Add("enliven");
                if (weapon.EffectTextBlob.Contains("Torpor", StringComparison.OrdinalIgnoreCase) ||
                    weapon.EffectTextBlob.Contains("Status Ailment: Torpor", StringComparison.OrdinalIgnoreCase)) cats.Add("torpor");
                if (weapon.EffectTextBlob.Contains("Applied Stats Debuff Tier Increased", StringComparison.OrdinalIgnoreCase)) cats.Add("applied_stats_debuff_tier_increased");
                if (weapon.EffectTextBlob.Contains("Applied Stats Buff Tier Increased", StringComparison.OrdinalIgnoreCase)) cats.Add("applied_stats_buff_tier_increased");
                if (weapon.EffectTextBlob.Contains("Amp Abilities", StringComparison.OrdinalIgnoreCase)) cats.Add("amp_abilities");
            }

            return cats;
        }

        private Dictionary<string, double> GetCategoryValueMapForWeapons(IEnumerable<WeaponOwnership> weapons, BattleContext context)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var weapon in weapons)
            {
                if (!_weaponCatalog.TryGetWeapon(weapon.WeaponName, out var info))
                {
                    continue;
                }

                var ob = weapon.OverboostLevel ?? 0;
                foreach (var category in GetSynergyCategories(info, context).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var value = GetWeaponCategoryContribution(info, ob, context, category);
                    if (value <= 0)
                    {
                        continue;
                    }

                    if (!map.TryGetValue(category, out var existing) || value > existing + 0.01)
                    {
                        map[category] = value;
                    }
                }
            }

            return map;
        }

        private static Dictionary<string, double> CombineCategoryValueMaps(Dictionary<string, double> first, Dictionary<string, double> second)
        {
            var combined = new Dictionary<string, double>(first, StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in second)
            {
                if (!combined.TryGetValue(kvp.Key, out var existing) || kvp.Value > existing + 0.01)
                {
                    combined[kvp.Key] = kvp.Value;
                }
            }

            return combined;
        }

        private static double CalculateCoverageValueGain(Dictionary<string, double> candidateCoverage, Dictionary<string, double> baselineCoverage)
        {
            double total = 0;

            foreach (var kvp in candidateCoverage)
            {
                var baseline = baselineCoverage.TryGetValue(kvp.Key, out var existing) ? existing : 0.0;
                if (kvp.Value > baseline + 0.01)
                {
                    total += kvp.Value - baseline;
                }
            }

            return Math.Round(total, 2);
        }

        private double GetWeaponCategoryContribution(WeaponInfo info, int overboostLevel, BattleContext context, string category)
        {
            var categories = GetSynergyCategories(info, context)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (categories.Count == 0 || !categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                return 0;
            }

            var totalSynergyScore = SynergyDetection.CalculateSynergyScore(info, overboostLevel, context);
            if (totalSynergyScore <= 0)
            {
                return 0;
            }

            var categoryShare = totalSynergyScore / categories.Count;
            var importance = SynergyDetection.GetSynergyCategoryWeight(category);
            var coverage = SynergyDetection.GetSynergyCoverageWeight(info, context);
            var reliabilityMultiplier = Math.Clamp(0.90 + ((coverage - 1.0) * 0.35), 0.82, 1.10);
            return Math.Round(categoryShare * importance * reliabilityMultiplier, 2);
        }

        private static double GetSynergyStackingMultiplier(int index)
        {
            return index switch
            {
                0 => 1.00,
                1 => 0.86,
                2 => 0.74,
                3 => 0.63,
                4 => 0.54,
                5 => 0.46,
                _ => 0.40
            };
        }

        private List<string> OptimizeNonDpsWeaponSelections(
            IEnumerable<string> team,
            Dictionary<string, List<WeaponOwnership>> weaponsByCharacter,
            Dictionary<string, CharacterScoreBreakdown> characterBreakdowns,
            BattleContext context)
        {
            var updatedCharacters = new List<string>();

            var weaponsByCharacterName = new Dictionary<string, List<WeaponOwnership>>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in team)
            {
                if (!characterBreakdowns.TryGetValue(ch, out var breakdown))
                {
                    continue;
                }

                // Extract the actual selected weapons from the breakdown
                var selectedWeapons = new List<WeaponOwnership>();
                foreach (var weaponBreakdown in breakdown.SelectedWeapons)
                {
                    if (!weaponsByCharacter.TryGetValue(ch, out var owned))
                    {
                        continue;
                    }

                    var matchingWeapon = owned.FirstOrDefault(w => w.WeaponName.Equals(weaponBreakdown.WeaponName, StringComparison.OrdinalIgnoreCase));
                    if (matchingWeapon != null)
                    {
                        selectedWeapons.Add(matchingWeapon);
                    }
                }

                weaponsByCharacterName[ch] = selectedWeapons;
            }

            // Re-evaluate non-DPS characters to find weapons with unique synergies
            foreach (var ch in team)
            {
                var role = CharacterRoleRegistry.GetRoleOrDefault(ch);
                
                // Only re-evaluate non-DPS characters
                if (role == CharacterRole.DPS)
                {
                    continue;
                }

                if (!weaponsByCharacter.TryGetValue(ch, out var owned) || owned.Count == 0)
                {
                    continue;
                }

                if (!weaponsByCharacterName.TryGetValue(ch, out var currentSelection) || currentSelection.Count == 0)
                {
                    continue;
                }

                // Collect synergies provided by OTHER team members (not this character)
                var otherTeamWeapons = new List<WeaponOwnership>();
                foreach (var otherCh in team)
                {
                    if (otherCh.Equals(ch, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Skip current character
                    }

                    if (!weaponsByCharacterName.TryGetValue(otherCh, out var otherWeapons))
                    {
                        continue;
                    }

                    foreach (var w in otherWeapons)
                    {
                        otherTeamWeapons.Add(w);
                    }
                }

                var otherTeamCategoryCoverage = GetCategoryValueMapForWeapons(otherTeamWeapons, context);

                // Try to find alternative weapons with more unique synergies
                var allowed = owned
                    .Where(w => _weaponCatalog.TryGetWeapon(w.WeaponName, out var info) && IsAllowedForMainOrOffHand(info))
                    .ToList();

                if (allowed.Count <= currentSelection.Count)
                {
                    continue; // No alternatives available
                }

                // Score each weapon by weighted coverage gain plus real utility profile.
                var alternativeScores = new List<(WeaponOwnership Weapon, double CoverageValue, double UtilityProfile, WeaponScoreBreakdown Breakdown, Dictionary<string, double> CategoryValues)>();
                
                foreach (var w in allowed)
                {
                    if (_weaponCatalog.TryGetWeapon(w.WeaponName, out var info))
                    {
                        var categoryValues = GetCategoryValueMapForWeapons(new[] { w }, context);
                        var coverageValue = CalculateCoverageValueGain(categoryValues, otherTeamCategoryCoverage);
                        var scoreBreakdown = ScoreWeapon(w, context, slot: "Utility");
                        var utilityProfile = scoreBreakdown.EffectScore + scoreBreakdown.PassiveScore + scoreBreakdown.CustomizationScore + scoreBreakdown.ReliabilityScore + (scoreBreakdown.DamageScore * 0.10);
                        alternativeScores.Add((w, coverageValue, utilityProfile, scoreBreakdown, categoryValues));
                    }
                }

                if (alternativeScores.Count == 0)
                {
                    continue;
                }

                var currentProfiles = currentSelection
                    .Select(w =>
                    {
                        var breakdown = ScoreWeapon(w, context, slot: "Utility");
                        var utility = breakdown.EffectScore + breakdown.PassiveScore + breakdown.CustomizationScore + breakdown.ReliabilityScore + (breakdown.DamageScore * 0.10);
                        return (Weapon: w, Breakdown: breakdown, Utility: utility);
                    })
                    .ToList();

                var currentUtilityProfile = currentProfiles.Sum(x => x.Utility);
                var currentCategoryCoverage = GetCategoryValueMapForWeapons(currentSelection, context);
                var currentCoverageValue = CalculateCoverageValueGain(currentCategoryCoverage, otherTeamCategoryCoverage);

                (WeaponOwnership MainWeapon, WeaponOwnership? OffWeapon, double CoverageValue, double UtilityProfile, double TotalWeaponScore)? bestCandidate = null;

                for (int i = 0; i < alternativeScores.Count; i++)
                {
                    for (int j = i + 1; j < alternativeScores.Count; j++)
                    {
                        var mainCandidate = alternativeScores[i];
                        var offCandidate = alternativeScores[j];

                        var combinedCategoryValues = CombineCategoryValueMaps(mainCandidate.CategoryValues, offCandidate.CategoryValues);
                        var coverageValue = CalculateCoverageValueGain(combinedCategoryValues, otherTeamCategoryCoverage);
                        var utilityProfile = mainCandidate.UtilityProfile + offCandidate.UtilityProfile;
                        var totalWeaponScore = mainCandidate.Breakdown.FinalWeaponScore + offCandidate.Breakdown.FinalWeaponScore;

                        if (bestCandidate == null ||
                            coverageValue > bestCandidate.Value.CoverageValue + 0.01 ||
                            (Math.Abs(coverageValue - bestCandidate.Value.CoverageValue) < 0.01 && utilityProfile > bestCandidate.Value.UtilityProfile + 0.01) ||
                            (Math.Abs(coverageValue - bestCandidate.Value.CoverageValue) < 0.01 && Math.Abs(utilityProfile - bestCandidate.Value.UtilityProfile) < 0.01 && totalWeaponScore > bestCandidate.Value.TotalWeaponScore + 0.01))
                        {
                            bestCandidate = (mainCandidate.Weapon, offCandidate.Weapon, coverageValue, utilityProfile, totalWeaponScore);
                        }
                    }
                }

                if (bestCandidate == null)
                {
                    continue;
                }

                var currentTotalWeaponScore = currentSelection.Sum(w => ScoreWeapon(w, context, slot: "Utility").FinalWeaponScore);
                var coverageGain = bestCandidate.Value.CoverageValue - currentCoverageValue;
                var utilityGain = bestCandidate.Value.UtilityProfile - currentUtilityProfile;
                var scoreDelta = bestCandidate.Value.TotalWeaponScore - currentTotalWeaponScore;

                var shouldSwap = coverageGain >= 45 &&
                                 utilityGain >= 14 &&
                                 scoreDelta >= -30;

                if (!shouldSwap && coverageGain >= 80 && utilityGain >= 6 && scoreDelta >= -12)
                {
                    shouldSwap = true;
                }

                if (shouldSwap)
                {
                    var orderedSelection = new[] { bestCandidate.Value.MainWeapon, bestCandidate.Value.OffWeapon }
                        .Where(w => w != null)
                        .Select(w => w!)
                        .OrderByDescending(w => ScoreWeapon(w, context, slot: "Main-hand").FinalWeaponScore)
                        .ToList();

                    var newSelection = new List<WeaponOwnership> { orderedSelection[0] };
                    if (orderedSelection.Count > 1)
                    {
                        newSelection.Add(orderedSelection[1]);
                    }

                    // Update the character breakdown with new weapon selection
                    if (characterBreakdowns.TryGetValue(ch, out var breakdown))
                    {
                        var newWeaponBreakdowns = new List<WeaponScoreBreakdown>();
                        
                        var mainBreakdown = ScoreWeapon(newSelection[0], context, slot: "Main-hand");
                        mainBreakdown.Slot = "Main-hand";
                        mainBreakdown.SelectionReason = $"Re-optimized for stronger utility profile (+{utilityGain:N1}) and weighted coverage gain (+{coverageGain:N1})";
                        newWeaponBreakdowns.Add(mainBreakdown);

                        if (newSelection.Count > 1)
                        {
                            var offBreakdown = ScoreWeapon(newSelection[1], context, slot: "Off-hand");
                            offBreakdown.Slot = "Off-hand";
                            offBreakdown.SelectionReason = $"Re-optimized for reliable support value while keeping score loss bounded ({scoreDelta:N1} score delta)";
                            newWeaponBreakdowns.Add(offBreakdown);
                        }

                        var newWeaponScore = newWeaponBreakdowns.Sum(w => w.FinalWeaponScore);
                        var newBasePlusGear = newWeaponScore + breakdown.CostumeScoreSum;

                        breakdown.SelectedWeapons = newWeaponBreakdowns;
                        breakdown.RawWeaponScoreSum = newWeaponScore;
                        breakdown.BasePlusGearScore = newBasePlusGear;
                        breakdown.FinalCharacterScore = newBasePlusGear * breakdown.RoleWeight;

                        // Update the original weaponsByCharacter dictionary for team synergy calculation
                        weaponsByCharacter[ch] = newSelection;
                        
                        // Track that this character was updated
                        updatedCharacters.Add(ch);
                    }
                }
            }

            return updatedCharacters;
        }
    }
}
