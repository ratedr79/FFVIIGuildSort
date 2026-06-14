using System.Text.Json;
using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    // Result of running the V2 engine across every survey account. Carries the per-account BestTeamResult list
    // (shaped identically to the legacy Gb20Analyzer output so the page's downstream sort / guild-lock / render /
    // CSV path is untouched) plus a diagnostics counter of survey gear names that could not be resolved to a V2 Id.
    public sealed class PowerLevelAnalyzerV2RunResult
    {
        public List<BestTeamResult> Results { get; set; } = new();
        public int TotalUnresolvedNames { get; set; }
    }

    // Adapter that runs the Player Power Analyzer V2 engine over GB-survey AccountRows and emits legacy-shaped
    // BestTeamResults. It NEVER modifies the V2 engine; it only calls the public Analyze(...) entry point.
    public sealed class PowerLevelAnalyzerV2Adapter
    {
        private readonly PlayerPowerAnalyzerV2Service _v2Service;
        private readonly WeaponCatalog _weaponCatalog;
        private readonly WeaponSearchDataService _weaponSearchDataService;
        private readonly NameCorrectionService _nameCorrectionService;
        private readonly TeamOptimizer _teamOptimizer;

        // Maximum fraction by which the reused legacy per-account bonus (materia + memoria + summon/enemy-skill) can
        // lift a V2 team score. Named + tunable. 0.30 = a fully-kitted account's bonus adds at most +30% to its V2
        // damage headline; an account with no bonus items adds nothing.
        public const double MaxBonusUplift = 0.30;

        public PowerLevelAnalyzerV2Adapter(
            PlayerPowerAnalyzerV2Service v2Service,
            WeaponCatalog weaponCatalog,
            WeaponSearchDataService weaponSearchDataService,
            NameCorrectionService nameCorrectionService,
            TeamOptimizer teamOptimizer)
        {
            _v2Service = v2Service;
            _weaponCatalog = weaponCatalog;
            _weaponSearchDataService = weaponSearchDataService;
            _nameCorrectionService = nameCorrectionService;
            _teamOptimizer = teamOptimizer;
        }

        public PowerLevelAnalyzerV2RunResult Analyze(IReadOnlyList<AccountRow> accounts, BattleContext context)
        {
            context ??= new BattleContext();
            var request = BuildRequest(context);

            var results = new List<BestTeamResult>();
            var totalUnresolved = 0;

            foreach (var account in accounts)
            {
                var inventory = BuildInventoryJson(account);
                totalUnresolved += inventory.UnresolvedCount;

                // Component breakdown (memoria/materia/utility + total) drives both the bounded uplift fraction
                // and the V2 "View details" panel, so the panel's bonus inputs match the uplift math exactly.
                var bonusBreakdown = _teamOptimizer.ComputeAccountBonusBreakdown(account, context);
                var bonusFraction = ComputeBonusFraction(bonusBreakdown.TotalBonus);

                PlayerPowerAnalyzerV2Result v2Result;
                try
                {
                    v2Result = _v2Service.Analyze(inventory.Json, request);
                }
                catch
                {
                    v2Result = new PlayerPowerAnalyzerV2Result();
                }

                results.Add(MapToBestTeamResult(account, v2Result, bonusFraction, bonusBreakdown, context, inventory.UnresolvedNames));
            }

            return new PowerLevelAnalyzerV2RunResult
            {
                Results = results,
                TotalUnresolvedNames = totalUnresolved
            };
        }

        // Offline guild ranking opt-in. BOTH knobs apply to the offline ranking ONLY — the interactive page and the
        // 422k repro keep the engine defaults (MainSeedTopN=1, mode cap) and stay byte-identical. MainSeedTopN=2
        // widens each character's top-2 distinct mains so a team-optimal lower-ranked main can anchor; skeleton cap
        // 250 (min recovering cap 224; 250 = margin) keeps the strong-but-lower-ranked synergy skeletons through the
        // cut — expensive (~30min/70 accts), hence offline-only. Together: whale DDelaneyCA (gb24) -> Aerith/Tifa/Zack
        // 385,379 / rank #1, zero regressions. Nullable + internal so the structural tests null both for the fast
        // default path; the whale-monotonicity test keeps them. (Promoting N=2 to the global default is deferred
        // pending the spread-vs-Vincent model audit.)
        internal int? GuildRankingMainSeedTopN { get; set; } = 2;
        internal int? GuildRankingSkeletonExpansionLimit { get; set; } = 250;

        // (B) Map the page's existing battle params to a V2 request. Fast search mode per spec; the rest stay at the
        // V2 defaults (Backbone sub-weapon strategy, default off-element factor, no boss immunities, etc.).
        private PlayerPowerAnalyzerV2Request BuildRequest(BattleContext context)
        {
            return new PlayerPowerAnalyzerV2Request
            {
                EnemyWeakness = context.EnemyWeakness,
                PreferredDamageType = context.PreferredDamageType,
                TargetScenario = context.TargetScenario,
                SearchMode = PlayerPowerAnalyzerV2SearchMode.Fast,
                MainSeedTopNOverride = GuildRankingMainSeedTopN,
                SkeletonExpansionLimitOverride = GuildRankingSkeletonExpansionLimit,
                EnabledTeamTemplates = context.EnabledTeamTemplates?.ToList() ?? new List<string>()
            };
        }

        // (A) Survey-row -> V2 localInventoryStateJson. For each owned gear column, recognize weapon vs costume the
        // same way the legacy path does (WeaponCatalog.TryGetWeapon / TryGetCostume), then resolve the name to a V2 Id
        // via WeaponSearchDataService.TryGetWeaponSearchItemByName(NameCorrection...(columnName)). Names that still
        // don't resolve are skipped and counted.
        internal InventoryBuildResult BuildInventoryJson(AccountRow account)
        {
            var weapons = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var costumes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            // Owned gear column names that recognized as weapon/costume but did NOT resolve to a V2 Id. This is
            // the V2 analog of the legacy "missing weaponData.tsv items" and is surfaced in the V2 Breakdown.
            var unresolvedNames = new List<string>();

            foreach (var kvp in account.ItemResponsesByColumnName)
            {
                var columnName = kvp.Key;
                var rawValue = kvp.Value;

                // Costume? (owned-or-not only).
                if (_weaponCatalog.TryGetCostume(columnName, out _))
                {
                    if (!IsCostumeOwned(rawValue))
                    {
                        continue;
                    }

                    var costumeId = ResolveCostumeId(columnName);
                    if (costumeId == null)
                    {
                        unresolvedNames.Add(columnName);
                        continue;
                    }

                    costumes[costumeId] = new { owned = true };
                    continue;
                }

                // Weapon (includes Ultimates; their value "Own" -> owned weapon entry).
                if (_weaponCatalog.TryGetWeapon(columnName, out _))
                {
                    var ownership = TranslateWeaponOwnership(rawValue);
                    if (ownership == null)
                    {
                        continue; // "Do Not Own" / unparseable -> skip (not unresolved).
                    }

                    var weaponId = ResolveWeaponId(columnName);
                    if (weaponId == null)
                    {
                        unresolvedNames.Add(columnName);
                        continue;
                    }

                    // level omitted (survey has none) -> V2 defaults to MaxWeaponLevel (140).
                    weapons[weaponId] = new { ownership, level = (int?)null };
                    continue;
                }

                // Not a weapon/costume column (summon, memoria, materia, metadata, etc.) -> not gear, ignore.
            }

            var state = new { weapons, costumes };
            var json = JsonSerializer.Serialize(state);
            return new InventoryBuildResult(json, unresolvedNames);
        }

        private string? ResolveWeaponId(string columnName)
        {
            var corrected = _nameCorrectionService.CorrectWeaponName(columnName);
            return _weaponSearchDataService.TryGetWeaponSearchItemByName(corrected)?.Id;
        }

        private string? ResolveCostumeId(string columnName)
        {
            var corrected = _nameCorrectionService.CorrectOutfitName(columnName);
            return _weaponSearchDataService.TryGetWeaponSearchItemByName(corrected)?.Id;
        }

        private static bool IsCostumeOwned(string rawValue)
        {
            return rawValue.Equals("Own", StringComparison.OrdinalIgnoreCase)
                   || rawValue.Equals("Owned", StringComparison.OrdinalIgnoreCase);
        }

        // Translate a survey weapon ownership value into the V2 inventory ownership token (V2 ParseOwnedOverboost
        // lowercases its input): "Do Not Own" -> skip; "Own"/"Owned"/"5 Star"/"5*" -> "own"; "OBn" -> "obn".
        private static string? TranslateWeaponOwnership(string rawValue)
        {
            var raw = (rawValue ?? string.Empty).Trim();
            if (raw.Length == 0 || raw.Equals("Do Not Own", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (raw.Equals("Own", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("Owned", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("5 Star", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("5★", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("5*", StringComparison.OrdinalIgnoreCase))
            {
                return "own";
            }

            if (raw.StartsWith("OB", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(raw[2..], out var ob)
                && ob >= 0
                && ob <= 10)
            {
                return $"ob{ob}";
            }

            return null;
        }

        // (C) Reused legacy per-account bonus -> bounded uplift fraction.
        // bonusFraction = Clamp((accountBonus / maxPossibleBonus) * MaxBonusUplift, 0, MaxBonusUplift).
        private static double ComputeBonusFraction(double accountBonus)
        {
            var raw = (accountBonus / TeamOptimizer.MaxPossibleAccountBonus) * MaxBonusUplift;
            return Math.Clamp(raw, 0.0, MaxBonusUplift);
        }

        // (D) V2Result -> legacy-shaped BestTeamResult, with the bonus uplift applied to the V2 score. On a
        // placeholder / no-result account (fewer than 3 eligible characters, or any failure) still emit a row with
        // Score = 0 so the account appears and sorts to the bottom.
        private static BestTeamResult MapToBestTeamResult(
            AccountRow account,
            PlayerPowerAnalyzerV2Result v2Result,
            double bonusFraction,
            AccountBonusBreakdown bonusBreakdown,
            BattleContext context,
            List<string> unresolvedNames)
        {
            if (!v2Result.HasResult || v2Result.IsPlaceholder || v2Result.TeamCharacters.Count == 0)
            {
                // Placeholder / no-result: leave Breakdown null so "View details" stays hidden and the row sorts
                // to the bottom at Score 0 (same fallback as before).
                return new BestTeamResult
                {
                    InGameName = account.InGameName,
                    DiscordName = account.DiscordName,
                    Score = 0,
                    Characters = new List<string>(),
                    Breakdown = null
                };
            }

            var score = Math.Round(v2Result.Score * (1 + bonusFraction), 2);

            return new BestTeamResult
            {
                InGameName = account.InGameName,
                DiscordName = account.DiscordName,
                Score = score,
                Characters = v2Result.TeamCharacters.ToList(),
                AlternateTeams = v2Result.AlternateTeams
                    .Select(a => new AlternateTeamResult
                    {
                        Characters = a.Characters.ToList(),
                        Score = a.Score
                    })
                    .ToList(),
                WeaponsByCharacter = BuildWeaponsByCharacter(v2Result),
                Breakdown = BuildV2Breakdown(account, v2Result, bonusBreakdown, context, unresolvedNames)
            };
        }

        // V2 "View details" panel content. The panel was designed for the legacy additive model, so the
        // AppliedRules note clarifies the V2 multiplicative meaning: TeamScore is the V2 team-damage score
        // BEFORE the uplift, and the memoria/materia/utility numbers below are the INPUTS to that uplift
        // (not additive points). SynergyBonus is omitted (V2 has no legacy synergy concept; the panel gates
        // it on > 0). Bonus components come straight from the shared ComputeAccountBonusBreakdown so they
        // match the legacy detail exactly.
        private static TeamScoreBreakdown BuildV2Breakdown(
            AccountRow account,
            PlayerPowerAnalyzerV2Result v2Result,
            AccountBonusBreakdown bonusBreakdown,
            BattleContext context,
            List<string> unresolvedNames)
        {
            var appliedRules = new List<string>
            {
                "V2 engine — player score = team damage × (1 + bonus uplift, capped 30%). Team Score shown is the team damage before the uplift; the memoria/materia/utility bonuses below are the inputs to that uplift, not additive points.",
                context.EnemyWeakness == Element.None ? "EnemyWeakness=None" : $"EnemyWeakness={context.EnemyWeakness}",
                context.PreferredDamageType == DamageType.Any ? "PreferredDamageType=Any" : $"PreferredDamageType={context.PreferredDamageType}"
            };

            if (!string.IsNullOrWhiteSpace(v2Result.MatchedTemplateName))
            {
                appliedRules.Add($"MatchedTemplate={v2Result.MatchedTemplateName}");
            }

            return new TeamScoreBreakdown
            {
                InGameName = account.InGameName,
                // V2 raw team-damage score (gear/team strength) BEFORE the bonus uplift.
                TeamScore = Math.Round(v2Result.Score, 2),
                EnemyWeakness = context.EnemyWeakness,
                PreferredDamageType = context.PreferredDamageType,
                MaxDpsAllowed = 2,
                // V2 has no legacy synergy concept; the panel gates SynergyBonus on > 0, so leaving it 0 hides it.
                SynergyBonus = 0,
                SynergyNotes = new List<string>(),
                // Player's actual bonus inputs, identical to the legacy detail numbers (shared helper).
                SelectedMemoria = bonusBreakdown.SelectedMemoria,
                MemoriaScore = bonusBreakdown.MemoriaScore,
                Materia = bonusBreakdown.Materia,
                MateriaScore = bonusBreakdown.MateriaScore,
                SelectedUtilityItems = bonusBreakdown.SelectedUtilityItems,
                UtilityScore = bonusBreakdown.UtilityScore,
                // V2 analog of legacy "missing weaponData.tsv items": owned gear names that didn't resolve to a V2 Id.
                MissingCatalogItems = (unresolvedNames ?? new List<string>())
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Select(n => new MissingCatalogItemBreakdown
                    {
                        ColumnName = n,
                        RawValue = account.ItemResponsesByColumnName.TryGetValue(n, out var rv) ? rv : string.Empty,
                        InferredKind = "Unresolved",
                        Notes = "Owned gear recognized in the survey but its name did not resolve to a V2 catalog Id, so it was excluded from the V2 team build."
                    })
                    .ToList(),
                Characters = BuildCharacterBreakdowns(v2Result),
                AppliedRules = appliedRules
            };
        }

        // Best-effort per-character breakdown from the V2 character builds, mapped into the panel's
        // CharacterScoreBreakdown shape. Thin by design: V2 exposes name/role/score, main/off/ultimate weapon
        // slots, and the chosen main/sub outfits, so we surface those (selected weapons + ultimate + the chosen
        // costumes + the per-char score) and leave the remaining legacy-only fields (considered list,
        // base-plus-gear math) at their defaults.
        private static List<CharacterScoreBreakdown> BuildCharacterBreakdowns(PlayerPowerAnalyzerV2Result v2Result)
        {
            var list = new List<CharacterScoreBreakdown>();

            foreach (var character in v2Result.Characters)
            {
                var selected = new List<WeaponScoreBreakdown>();
                AddWeaponBreakdown(selected, character.MainWeapon, "Main-hand");
                AddWeaponBreakdown(selected, character.OffHandWeapon, "Off-hand");

                WeaponScoreBreakdown? ultimate = null;
                if (character.UltimateWeapon != null && !string.IsNullOrWhiteSpace(character.UltimateWeapon.Name))
                {
                    ultimate = ToWeaponBreakdown(character.UltimateWeapon, "Ultimate");
                }

                var rawWeaponScoreSum = Math.Round(selected.Sum(w => w.FinalWeaponScore), 2);

                // Map the V2-chosen outfits into the legacy SelectedCostumes shape the per-character panel
                // (PowerLevelAnalyzer.cshtml: ch.SelectedCostumes / ch.CostumeScoreSum) already renders. The
                // legacy slot labels are "Main"/"Sub"; we mirror those so the V2 path surfaces costumes in the
                // exact place the legacy path does.
                var costumes = new List<CostumeScoreBreakdown>();
                AddCostumeBreakdown(costumes, character.MainOutfit, "Main");
                foreach (var subOutfit in character.SubOutfits)
                {
                    AddCostumeBreakdown(costumes, subOutfit, "Sub");
                }

                var costumeScoreSum = Math.Round(costumes.Sum(c => c.FinalCostumeScore), 2);

                list.Add(new CharacterScoreBreakdown
                {
                    CharacterName = character.CharacterName,
                    Role = character.Role,
                    RoleWeight = TeamOptimizer.GetRoleWeight(character.Role),
                    SelectedWeapons = selected,
                    UltimateWeapon = ultimate,
                    SelectedCostumes = costumes,
                    CostumeScoreSum = costumeScoreSum,
                    RawWeaponScoreSum = rawWeaponScoreSum,
                    BasePlusGearScore = rawWeaponScoreSum,
                    FinalCharacterScore = Math.Round(character.Score, 2)
                });
            }

            return list;
        }

        private static void AddCostumeBreakdown(List<CostumeScoreBreakdown> list, PlayerPowerAnalyzerV2ItemSlot? slot, string slotName)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.Name))
            {
                return;
            }

            list.Add(new CostumeScoreBreakdown
            {
                CostumeName = slot.Name,
                Slot = slotName,
                FinalCostumeScore = Math.Round(slot.Score, 2)
            });
        }

        private static void AddWeaponBreakdown(List<WeaponScoreBreakdown> list, PlayerPowerAnalyzerV2ItemSlot? slot, string slotName)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.Name))
            {
                return;
            }

            list.Add(ToWeaponBreakdown(slot, slotName));
        }

        private static WeaponScoreBreakdown ToWeaponBreakdown(PlayerPowerAnalyzerV2ItemSlot slot, string slotName)
        {
            return new WeaponScoreBreakdown
            {
                WeaponName = slot.Name,
                Slot = slotName,
                OverboostLevel = slot.OverboostLevel,
                IsUltimate = slot.IsUltimate,
                FinalWeaponScore = Math.Round(slot.Score, 2)
            };
        }

        // Thin best-effort per-character gear breakdown so the gear panel has something to render. Non-critical:
        // populated from the V2 main/off-hand/ultimate slots into the legacy WeaponOwnership shape.
        private static Dictionary<string, List<WeaponOwnership>> BuildWeaponsByCharacter(PlayerPowerAnalyzerV2Result v2Result)
        {
            var map = new Dictionary<string, List<WeaponOwnership>>(StringComparer.OrdinalIgnoreCase);

            foreach (var character in v2Result.Characters)
            {
                var list = new List<WeaponOwnership>();
                AddSlot(list, character.CharacterName, character.MainWeapon);
                AddSlot(list, character.CharacterName, character.OffHandWeapon);
                AddSlot(list, character.CharacterName, character.UltimateWeapon);
                foreach (var sub in character.SubWeapons)
                {
                    AddSlot(list, character.CharacterName, sub);
                }

                if (list.Count > 0)
                {
                    map[character.CharacterName] = list;
                }
            }

            return map;
        }

        private static void AddSlot(List<WeaponOwnership> list, string character, PlayerPowerAnalyzerV2ItemSlot? slot)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.Name))
            {
                return;
            }

            list.Add(new WeaponOwnership
            {
                WeaponName = slot.Name,
                CharacterName = character,
                IsUltimate = slot.IsUltimate,
                OverboostLevel = slot.OverboostLevel
            });
        }

        internal readonly record struct InventoryBuildResult(string Json, List<string> UnresolvedNames)
        {
            public int UnresolvedCount => UnresolvedNames.Count;
        }
    }
}
