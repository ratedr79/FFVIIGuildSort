using FFVIIEverCrisisAnalyzer.Models;

namespace FFVIIEverCrisisAnalyzer.Services
{
    public static class SynergyDetection
    {
        private sealed record Match(string Key, string Reason);

        private enum Tier
        {
            None = 0,
            Low = 1,
            Mid = 2,
            High = 3,
            ExtraHigh = 4
        }

        private static bool HasToken(WeaponInfo weapon, string token)
        {
            return !string.IsNullOrWhiteSpace(weapon.EffectTextBlob) &&
                   weapon.EffectTextBlob.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyToken(WeaponInfo weapon, params string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return false;
            }

            foreach (var token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (HasToken(weapon, token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasToken(string? effectTextBlob, string token)
        {
            return !string.IsNullOrWhiteSpace(effectTextBlob) &&
                   effectTextBlob.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyToken(string? effectTextBlob, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(effectTextBlob) || tokens == null || tokens.Length == 0)
                return false;
            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) && effectTextBlob.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool ProvidesWeaknessUtility(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return false;
            }

            // Heuristic based on Effect text fields in weaponData.tsv.
            // We look for patterns like "Fire Resistance Down" or "Status Ailment: Ice Weakness".
            var tokens = weapon.EffectTextBlob;
            if (string.IsNullOrWhiteSpace(tokens))
            {
                return false;
            }

            var elementName = weakness.ToString();
            if (tokens.Contains($"{elementName} Resistance Down", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (tokens.Contains($"Status Ailment: {elementName} Weakness", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (tokens.Contains($"{elementName} Damage Bonus", StringComparison.OrdinalIgnoreCase) ||
                tokens.Contains($"{elementName} Damage Up", StringComparison.OrdinalIgnoreCase) ||
                tokens.Contains($"{elementName} Damage Boost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool ProvidesElementalDamageBoost(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return false;
            }

            var elementName = weakness.ToString();
            return HasToken(weapon, $"{elementName} Damage Bonus") ||
                   HasToken(weapon, $"{elementName} Damage Up");
        }

        public static bool ProvidesElementalDamageBonus(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return false;
            }

            var elementName = weakness.ToString();
            return HasToken(weapon, $"{elementName} Damage Bonus");
        }

        public static bool ProvidesElementalDamageUp(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return false;
            }

            var elementName = weakness.ToString();
            return HasToken(weapon, $"{elementName} Damage Up");
        }

        public static bool ProvidesElementalResistanceDown(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return false;
            }

            var elementName = weakness.ToString();
            return HasToken(weapon, $"{elementName} Resistance Down") ||
                   HasToken(weapon, $"Status Ailment: {elementName} Weakness");
        }

        public static bool ProvidesDefenseDown(WeaponInfo weapon, DamageType preferred)
        {
            // Bosses can be resistant, so this is considered weaker utility.
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "PDEF Down"),
                DamageType.Magical => HasToken(weapon, "MDEF Down"),
                _ => HasToken(weapon, "PDEF Down") || HasToken(weapon, "MDEF Down")
            };
        }

        public static bool ProvidesEnfeeble(WeaponInfo weapon)
        {
            // Enfeeble reduces all debuffs on an enemy by one tier.
            // Some bosses are resistant, so we score this slightly lower than top-tier debuff enablers.
            return HasAnyToken(weapon, "Enfeeble", "Status Ailment: Enfeeble");
        }

        public static bool ProvidesAppliedStatsDebuffTierIncreased(WeaponInfo weapon)
        {
            // This effect increases the current applied debuffs on an enemy by the Pot tier,
            // up to the PotMax tier. This can amplify multiple debuffs simultaneously.
            return HasToken(weapon, "Applied Stats Debuff Tier Increased");
        }

        public static bool ProvidesAppliedStatsBuffTierIncreased(WeaponInfo weapon)
        {
            // This effect increases the current applied buffs on allies by the Pot tier,
            // up to the PotMax tier. This can amplify multiple buffs simultaneously.
            return HasToken(weapon, "Applied Stats Buff Tier Increased");
        }

        public static bool ProvidesEnliven(WeaponInfo weapon)
        {
            // Enliven raises any existing damage buffs on a character by two levels.
            // Does not add buffs, only amplifies existing ones, so requires team synergy.
            return HasAnyToken(weapon, "Enliven", "Status Ailment: Enliven");
        }

        public static bool ProvidesTorpor(WeaponInfo weapon)
        {
            // Torpor temporarily incapacitates a target and increases damage taken.
            // Current known source is short duration (~8s), so we value it below the strongest long-lived debuffs.
            return HasAnyToken(weapon, "Torpor", "Status Ailment: Torpor");
        }

        public static bool ProvidesWeaponBoost(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "Phys. Weapon Boost"),
                DamageType.Magical => HasToken(weapon, "Mag. Weapon Boost"),
                _ => HasToken(weapon, "Phys. Weapon Boost") || HasToken(weapon, "Mag. Weapon Boost")
            };
        }

        public static bool ProvidesElementalWeaponBoost(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return false;
            }

            var elementName = weakness.ToString();
            return HasToken(weapon, $"{elementName} Weapon Boost");
        }

        public static bool ProvidesDamageReceivedUp(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "Phys. Dmg. Rcvd. Up"),
                DamageType.Magical => HasToken(weapon, "Mag. Dmg. Rcvd. Up"),
                _ => HasToken(weapon, "Phys. Dmg. Rcvd. Up") || HasToken(weapon, "Mag. Dmg. Rcvd. Up")
            };
        }

        public static bool ProvidesSingleTargetDamageReceivedUp(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasAnyToken(weapon, "Single-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: Single-Tgt. Phys. Dmg. Rcvd. Up"),
                DamageType.Magical => HasAnyToken(weapon, "Single-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: Single-Tgt. Mag. Dmg. Rcvd. Up"),
                _ => HasAnyToken(weapon, "Single-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: Single-Tgt. Phys. Dmg. Rcvd. Up") ||
                     HasAnyToken(weapon, "Single-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: Single-Tgt. Mag. Dmg. Rcvd. Up")
            };
        }

        public static bool ProvidesAllTargetDamageReceivedUp(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasAnyToken(weapon, "All-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Phys. Dmg. Rcvd. Up"),
                DamageType.Magical => HasAnyToken(weapon, "All-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Mag. Dmg. Rcvd. Up"),
                _ => HasAnyToken(weapon, "All-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Phys. Dmg. Rcvd. Up") ||
                     HasAnyToken(weapon, "All-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Mag. Dmg. Rcvd. Up")
            };
        }

        public static bool ProvidesExploitWeakness(WeaponInfo weapon)
        {
            return HasToken(weapon, "Exploit Weakness");
        }

        public static bool ProvidesHaste(WeaponInfo weapon)
        {
            return HasToken(weapon, "Haste");
        }

        public static bool ProvidesAtbConservationEffect(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "Phys. ATB Conservation Effect"),
                DamageType.Magical => HasToken(weapon, "Mag. ATB Conservation Effect"),
                _ => HasToken(weapon, "Phys. ATB Conservation Effect") || HasToken(weapon, "Mag. ATB Conservation Effect")
            };
        }

        private static int GetAtbPlusAmount(WeaponInfo weapon)
        {
            if (string.IsNullOrWhiteSpace(weapon.EffectTextBlob))
            {
                return 0;
            }

            // weaponData.tsv uses tokens like "ATB+1", "ATB+3" inside EffectTextBlob.
            foreach (var part in weapon.EffectTextBlob.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith("ATB+", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var raw = part[4..].Trim();
                if (int.TryParse(raw, out var n))
                {
                    return Math.Max(0, n);
                }
            }

            return 0;
        }

        private static double ApplyObPotScaling(double ob10Pot, int overboostLevel)
        {
            if (overboostLevel >= 10)
            {
                return ob10Pot;
            }

            if (overboostLevel >= 6)
            {
                return ob10Pot * 0.90;
            }

            return ob10Pot * 0.80;
        }

        private static double? TryGetEffectPotScaled(string? effectTextBlob, string effectName, int overboostLevel)
        {
            if (string.IsNullOrWhiteSpace(effectTextBlob) || string.IsNullOrWhiteSpace(effectName))
            {
                return null;
            }

            foreach (var part in effectTextBlob.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith(effectName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var potIdx = part.IndexOf("Pot=", StringComparison.OrdinalIgnoreCase);
                if (potIdx < 0)
                {
                    continue;
                }

                var potRaw = part[(potIdx + 4)..].Trim();
                if (double.TryParse(potRaw.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var potParsed))
                {
                    return ApplyObPotScaling(potParsed, overboostLevel);
                }
            }

            return null;
        }

        private static bool ProvidesAmpAbilities(WeaponInfo weapon, DamageType preferred)
        {
            // Amp abilities are limited-use, so we only consider them when the team's preferred damage type matches.
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "Amp. Phys. Abilities"),
                DamageType.Magical => HasToken(weapon, "Amp. Mag. Abilities"),
                _ => false
            };
        }

        private static (double Pot, int Count)? TryGetAmpMeta(string? effectTextBlob, DamageType preferred, int overboostLevel)
        {
            if (string.IsNullOrWhiteSpace(effectTextBlob))
            {
                return null;
            }

            var ampToken = preferred switch
            {
                DamageType.Physical => "Amp. Phys. Abilities",
                DamageType.Magical => "Amp. Mag. Abilities",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(ampToken))
            {
                return null;
            }

            // WeaponCatalog appends meta parts like:
            // "Amp. Phys. Abilities Pot=30%" and "Amp. Phys. Abilities Count=3".
            double pot = 0;
            int count = 0;

            foreach (var part in effectTextBlob.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith(ampToken, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var potIdx = part.IndexOf("Pot=", StringComparison.OrdinalIgnoreCase);
                if (potIdx >= 0)
                {
                    var potRaw = part[(potIdx + 4)..].Trim();
                    if (double.TryParse(potRaw.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var potParsed))
                    {
                        pot = ApplyObPotScaling(potParsed, overboostLevel);
                    }
                }

                var countIdx = part.IndexOf("Count=", StringComparison.OrdinalIgnoreCase);
                if (countIdx >= 0)
                {
                    var countRaw = part[(countIdx + 6)..].Trim();
                    if (int.TryParse(countRaw, out var countParsed))
                    {
                        count = countParsed;
                    }
                }
            }

            if (pot <= 0 || count <= 0)
            {
                return null;
            }

            return (pot, count);
        }

        public static bool WeaponMatchesPreferredDamageType(WeaponInfo weapon, DamageType preferred)
        {
            if (preferred == DamageType.Any)
            {
                return true;
            }

            // weaponData uses Ability Type: Phys/Mag/Both
            var abilityType = (weapon.AbilityType ?? string.Empty).Trim();
            return preferred switch
            {
                DamageType.Physical => abilityType.Equals("Phys", StringComparison.OrdinalIgnoreCase) ||
                                       abilityType.Equals("Phys.", StringComparison.OrdinalIgnoreCase) ||
                                       abilityType.Equals("Both", StringComparison.OrdinalIgnoreCase) ||
                                       abilityType.Equals("Phys./Mag.", StringComparison.OrdinalIgnoreCase),
                DamageType.Magical => abilityType.Equals("Mag", StringComparison.OrdinalIgnoreCase) ||
                                      abilityType.Equals("Mag.", StringComparison.OrdinalIgnoreCase) ||
                                      abilityType.Equals("Both", StringComparison.OrdinalIgnoreCase) ||
                                      abilityType.Equals("Phys./Mag.", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        public static bool WeaponMatchesEnemyWeakness(WeaponInfo weapon, Element weakness)
        {
            if (weakness == Element.None)
            {
                return true;
            }

            // weaponData uses Ability Element field.
            if (string.IsNullOrWhiteSpace(weapon.AbilityElement) ||
                weapon.AbilityElement.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                weapon.AbilityElement.Equals("Non-Elemental", StringComparison.OrdinalIgnoreCase) ||
                weapon.AbilityElement.Equals("Non Elemental", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return weapon.AbilityElement.Equals(weakness.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool ProvidesDamageTypeBoost(WeaponInfo weapon, DamageType preferred)
        {
            if (preferred == DamageType.Any)
            {
                return false;
            }

            var tokens = weapon.EffectTextBlob;
            if (string.IsNullOrWhiteSpace(tokens))
            {
                return false;
            }

            return preferred switch
            {
                DamageType.Physical => tokens.Contains("PATK Up", StringComparison.OrdinalIgnoreCase) ||
                                       tokens.Contains("Phys. Damage Bonus", StringComparison.OrdinalIgnoreCase),
                DamageType.Magical => tokens.Contains("MATK Up", StringComparison.OrdinalIgnoreCase) ||
                                      tokens.Contains("Mag. Damage Bonus", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        public static bool ProvidesDamageTypeDamageBonus(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "Phys. Damage Bonus"),
                DamageType.Magical => HasToken(weapon, "Mag. Damage Bonus"),
                _ => HasToken(weapon, "Phys. Damage Bonus") || HasToken(weapon, "Mag. Damage Bonus")
            };
        }

        public static bool ProvidesDamageTypeAtkUp(WeaponInfo weapon, DamageType preferred)
        {
            return preferred switch
            {
                DamageType.Physical => HasToken(weapon, "PATK Up"),
                DamageType.Magical => HasToken(weapon, "MATK Up"),
                _ => HasToken(weapon, "PATK Up") || HasToken(weapon, "MATK Up")
            };
        }

        public static double CalculateSynergyScore(WeaponInfo weapon, int overboostLevel, BattleContext ctx)
        {
            double score = 0;

            static double ApplyBonus(BattleContext ctx, string key, double basePoints)
            {
                if (basePoints == 0)
                {
                    return 0;
                }

                if (ctx.SynergyEffectBonusPercents == null)
                {
                    return basePoints;
                }

                if (!ctx.SynergyEffectBonusPercents.TryGetValue(key, out var bonusPct))
                {
                    return basePoints;
                }

                if (bonusPct <= 0)
                {
                    return basePoints;
                }

                return basePoints * (1.0 + (bonusPct / 100.0));
            }

            // Highest value: element resistance down / weakness infliction.
            if (ProvidesElementalResistanceDown(weapon, ctx.EnemyWeakness))
            {
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, ctx.EnemyWeakness != Element.None ? $"{ctx.EnemyWeakness} Resistance Down" : null, overboostLevel);
                score += ApplyBonus(ctx, "ElementalResistanceDown", tier switch
                {
                    Tier.ExtraHigh => 320,
                    Tier.High => 290,
                    Tier.Mid => 260,
                    Tier.Low => 230,
                    _ => 250
                });
            }

            // ATB+N: tempo buff. Usually conditional, so score it below Haste but scale with N.
            var atbPlus = GetAtbPlusAmount(weapon);
            if (atbPlus > 0)
            {
                // Keep bounded. In the current TSV we mostly see +1 and +3.
                var n = Math.Min(5, atbPlus);

                // Use a conservative baseline and scale with N.
                // OB impacts reliability/strength; apply the same downgrade buckets.
                var obMult = overboostLevel >= 10 ? 1.0 : (overboostLevel >= 6 ? 0.85 : 0.70);

                var baseTempo = 41.25; // reduced 25%
                var perN = 15.0;
                score += ApplyBonus(ctx, "AtbPlus", (baseTempo + (perN * n)) * obMult);
            }

            // Next: element buffs. "Damage Bonus" is stronger/more specific than generic "Damage Up".
            if (ProvidesElementalDamageBonus(weapon, ctx.EnemyWeakness))
            {
                var token = ctx.EnemyWeakness != Element.None ? $"{ctx.EnemyWeakness} Damage Bonus" : string.Empty;
                var pot = TryGetEffectPotScaled(weapon.EffectTextBlob, token, overboostLevel);
                if (pot.HasValue)
                {
                    score += ApplyBonus(ctx, "ElementalDamageBonus", Math.Min(300, 9.0 * pot.Value));
                }
                else
                {
                    var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, token, overboostLevel);
                    score += ApplyBonus(ctx, "ElementalDamageBonus", tier switch
                    {
                        Tier.ExtraHigh => 250,
                        Tier.High => 230,
                        Tier.Mid => 210,
                        Tier.Low => 185,
                        _ => 210
                    });
                }
            }

            if (ProvidesElementalDamageUp(weapon, ctx.EnemyWeakness))
            {
                var token = ctx.EnemyWeakness != Element.None ? $"{ctx.EnemyWeakness} Damage Up" : string.Empty;
                var pot = TryGetEffectPotScaled(weapon.EffectTextBlob, token, overboostLevel);
                if (pot.HasValue)
                {
                    score += ApplyBonus(ctx, "ElementalDamageUp", Math.Min(260, 8.0 * pot.Value));
                }
                else
                {
                    var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, token, overboostLevel);
                    score += ApplyBonus(ctx, "ElementalDamageUp", tier switch
                    {
                        Tier.ExtraHigh => 215,
                        Tier.High => 195,
                        Tier.Mid => 170,
                        Tier.Low => 145,
                        _ => 170
                    });
                }
            }

            // High: weapon boosts and damage received up.
            if (ProvidesWeaponBoost(weapon, ctx.PreferredDamageType)) score += ApplyBonus(ctx, "WeaponBoost", 180);
            if (ProvidesElementalWeaponBoost(weapon, ctx.EnemyWeakness)) score += ApplyBonus(ctx, "ElementalWeaponBoost", 180);

            // Higher than generic: single-target damage received up is ideal for boss fights.
            if (ProvidesSingleTargetDamageReceivedUp(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType switch
                {
                    DamageType.Physical => "Single-Tgt. Phys. Dmg. Rcvd. Up",
                    DamageType.Magical => "Single-Tgt. Mag. Dmg. Rcvd. Up",
                    _ => string.Empty
                };

                var pot = !string.IsNullOrWhiteSpace(token)
                    ? TryGetEffectPotScaled(weapon.EffectTextBlob, token, overboostLevel)
                    : null;

                if (pot.HasValue)
                {
                    // Scale slightly above the generic received-up curve.
                    score += ApplyBonus(ctx, "SingleTargetDamageReceivedUp", Math.Min(360, 14.5 * pot.Value));
                }
                else
                {
                    score += ApplyBonus(ctx, "SingleTargetDamageReceivedUp", 260);
                }
            }

            // All-target damage received up: same tier as single-target, for AoE abilities.
            if (ProvidesAllTargetDamageReceivedUp(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType switch
                {
                    DamageType.Physical => "All-Tgt. Phys. Dmg. Rcvd. Up",
                    DamageType.Magical => "All-Tgt. Mag. Dmg. Rcvd. Up",
                    _ => string.Empty
                };

                var pot = !string.IsNullOrWhiteSpace(token)
                    ? TryGetEffectPotScaled(weapon.EffectTextBlob, token, overboostLevel)
                    : null;

                if (pot.HasValue)
                {
                    score += ApplyBonus(ctx, "AllTargetDamageReceivedUp", Math.Min(360, 14.5 * pot.Value));
                }
                else
                {
                    score += ApplyBonus(ctx, "AllTargetDamageReceivedUp", 260);
                }
            }

            if (ProvidesDamageReceivedUp(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType switch
                {
                    DamageType.Physical => "Phys. Dmg. Rcvd. Up",
                    DamageType.Magical => "Mag. Dmg. Rcvd. Up",
                    _ => string.Empty
                };

                var pot = !string.IsNullOrWhiteSpace(token)
                    ? TryGetEffectPotScaled(weapon.EffectTextBlob, token, overboostLevel)
                    : null;

                if (pot.HasValue)
                {
                    score += ApplyBonus(ctx, "DamageReceivedUp", Math.Min(320, 13.0 * pot.Value));
                }
                else
                {
                    score += ApplyBonus(ctx, "DamageReceivedUp", 235);
                }
            }

            // Medium: generic type bonuses and attack ups.
            if (ProvidesDamageTypeDamageBonus(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType switch
                {
                    DamageType.Physical => "Phys. Damage Bonus",
                    DamageType.Magical => "Mag. Damage Bonus",
                    _ => string.Empty
                };

                var pot = !string.IsNullOrWhiteSpace(token)
                    ? TryGetEffectPotScaled(weapon.EffectTextBlob, token, overboostLevel)
                    : null;

                if (pot.HasValue)
                {
                    score += ApplyBonus(ctx, "DamageTypeDamageBonus", Math.Min(200, 8.0 * pot.Value));
                }
                else
                {
                    score += ApplyBonus(ctx, "DamageTypeDamageBonus", 140);
                }
            }
            if (ProvidesDamageTypeAtkUp(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType == DamageType.Physical ? "PATK Up" : "MATK Up";
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, token, overboostLevel);
                score += ApplyBonus(ctx, "DamageTypeAtkUp", tier switch
                {
                    Tier.ExtraHigh => 150,
                    Tier.High => 130,
                    Tier.Mid => 110,
                    Tier.Low => 95,
                    _ => 110
                });
            }

            // Additional: exploit weakness is a strong self/party damage modifier.
            if (ProvidesExploitWeakness(weapon)) score += ApplyBonus(ctx, "ExploitWeakness", 220);

            // Haste: team tempo buff (more actions over time). Treat as a high-value buff.
            if (ProvidesHaste(weapon))
            {
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, "Haste", overboostLevel);
                score += ApplyBonus(ctx, "Haste", tier switch
                {
                    Tier.ExtraHigh => 220,
                    Tier.High => 195,
                    Tier.Mid => 170,
                    Tier.Low => 145,
                    _ => 170
                });
            }

            // ATB conservation: similar to Haste (more attacks over time), but narrower (phys-only or mag-only).
            if (ProvidesAtbConservationEffect(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType switch
                {
                    DamageType.Physical => "Phys. ATB Conservation Effect",
                    DamageType.Magical => "Mag. ATB Conservation Effect",
                    _ => string.Empty
                };

                var tier = !string.IsNullOrWhiteSpace(token)
                    ? GetEffectiveTierFromPotMax(weapon.EffectTextBlob, token, overboostLevel)
                    : Tier.None;

                score += ApplyBonus(ctx, "AtbConservationEffect", tier switch
                {
                    Tier.ExtraHigh => 185,
                    Tier.High => 165,
                    Tier.Mid => 145,
                    Tier.Low => 125,
                    _ => 145
                });
            }

            // Medium-high: Enfeeble (boss resistance exists).
            if (ProvidesEnfeeble(weapon)) score += ApplyBonus(ctx, "Enfeeble", 140);

            // Debuff amplifier: increases existing applied debuffs toward their max tier.
            // This is particularly valuable when the team is already applying multiple debuffs.
            if (ProvidesAppliedStatsDebuffTierIncreased(weapon))
            {
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, "Applied Stats Debuff Tier Increased", overboostLevel);
                score += ApplyBonus(ctx, "AppliedStatsDebuffTierIncreased", tier switch
                {
                    Tier.ExtraHigh => 210,
                    Tier.High => 185,
                    Tier.Mid => 160,
                    Tier.Low => 135,
                    _ => 160
                });
            }

            // Buff amplifier: increases existing applied buffs toward their max tier.
            // Treat similarly to the debuff amplifier, slightly lower because it depends on already having buffs up.
            if (ProvidesAppliedStatsBuffTierIncreased(weapon))
            {
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, "Applied Stats Buff Tier Increased", overboostLevel);
                score += ApplyBonus(ctx, "AppliedStatsBuffTierIncreased", tier switch
                {
                    Tier.ExtraHigh => 195,
                    Tier.High => 170,
                    Tier.Mid => 150,
                    Tier.Low => 125,
                    _ => 150
                });
            }

            // Enliven: raises existing damage buffs by 2 tiers. Requires team synergy to provide buffs.
            // Weighted slightly below damage buffs themselves.
            if (ProvidesEnliven(weapon))
            {
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, "Enliven", overboostLevel);
                score += ApplyBonus(ctx, "Enliven", tier switch
                {
                    Tier.ExtraHigh => 150,
                    Tier.High => 135,
                    Tier.Mid => 120,
                    Tier.Low => 105,
                    _ => 120
                });
            }

            // Torpor: short-duration status that increases damage taken (known current value ~50%).
            // Strong burst-window utility, but weighted below persistent received-up effects.
            if (ProvidesTorpor(weapon))
            {
                var pot = TryGetEffectPotScaled(weapon.EffectTextBlob, "Torpor", overboostLevel);
                if (pot.HasValue)
                {
                    score += ApplyBonus(ctx, "Torpor", Math.Min(260, 4.0 * pot.Value));
                }
                else
                {
                    var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, "Torpor", overboostLevel);
                    score += ApplyBonus(ctx, "Torpor", tier switch
                    {
                        Tier.ExtraHigh => 220,
                        Tier.High => 200,
                        Tier.Mid => 185,
                        Tier.Low => 165,
                        _ => 200
                    });
                }
            }

            // Medium: limited-use Amp abilities. Slightly lower weight than other top-tier buffs.
            // Base amp score is derived from Pot and Count, then scaled by 0.75.
            if (ProvidesAmpAbilities(weapon, ctx.PreferredDamageType))
            {
                var meta = TryGetAmpMeta(weapon.EffectTextBlob, ctx.PreferredDamageType, overboostLevel);
                if (meta != null)
                {
                    // Pot is a percent increase, Count is number of affected attacks.
                    // Keep it bounded so odd data doesn't blow up the score.
                    var potScore = Math.Min(60, meta.Value.Pot);
                    var countScore = Math.Min(6, meta.Value.Count) * 10;
                    score += ApplyBonus(ctx, "AmpAbilities", (potScore + countScore) * 0.75);
                }
                else
                {
                    score += ApplyBonus(ctx, "AmpAbilities", 90 * 0.75);
                }
            }

            // Lower: PDEF/MDEF down due to boss resist prevalence.
            if (ProvidesDefenseDown(weapon, ctx.PreferredDamageType))
            {
                var token = ctx.PreferredDamageType == DamageType.Physical ? "PDEF Down" : "MDEF Down";
                var tier = GetEffectiveTierFromPotMax(weapon.EffectTextBlob, token, overboostLevel);
                score += ApplyBonus(ctx, "DefenseDown", tier switch
                {
                    Tier.ExtraHigh => 125,
                    Tier.High => 110,
                    Tier.Mid => 95,
                    Tier.Low => 80,
                    _ => 90
                });
            }

            // Apply ability range weighting: broader targeting is generally more valuable.
            // Buff preference: All Allies > Single Ally > Self.
            // Debuff preference: All Enemies (or All Allies for ally-applied debuffs) > Single Enemy.
            // If we can't infer buff vs debuff, apply a small generic adjustment.
            score *= GetRangeWeightMultiplier(weapon, ctx);

            return score;
        }

        public static double GetSynergyCoverageWeight(WeaponInfo weapon, BattleContext ctx)
        {
            return GetRangeWeightMultiplier(weapon, ctx);
        }

        private static Tier GetEffectiveTierFromPotMax(string? effectTextBlob, string? effectName, int overboostLevel)
        {
            if (string.IsNullOrWhiteSpace(effectTextBlob) || string.IsNullOrWhiteSpace(effectName))
            {
                return Tier.None;
            }

            var maxTier = TryParsePotMaxTier(effectTextBlob, effectName);
            if (maxTier == Tier.None)
            {
                return Tier.None;
            }

            var downgrade = overboostLevel >= 10 ? 0 : (overboostLevel >= 6 ? 1 : 2);
            var effective = (int)maxTier - downgrade;
            if (effective < (int)Tier.Low)
            {
                effective = (int)Tier.Low;
            }

            if (effective > (int)maxTier)
            {
                effective = (int)maxTier;
            }

            return (Tier)effective;
        }

        private static Tier TryParsePotMaxTier(string effectTextBlob, string effectName)
        {
            foreach (var part in effectTextBlob.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith(effectName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var idx = part.IndexOf("PotMax=", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    continue;
                }

                var raw = part[(idx + 7)..].Trim();
                if (raw.Equals("Low", StringComparison.OrdinalIgnoreCase)) return Tier.Low;
                if (raw.Equals("Mid", StringComparison.OrdinalIgnoreCase)) return Tier.Mid;
                if (raw.Equals("High", StringComparison.OrdinalIgnoreCase)) return Tier.High;
                if (raw.Equals("Extra High", StringComparison.OrdinalIgnoreCase) || raw.Equals("ExtraHigh", StringComparison.OrdinalIgnoreCase)) return Tier.ExtraHigh;

                if (double.TryParse(raw.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                {
                    // Fallback heuristic if TSV provides numeric PotMax: map to closest tier by effect family.
                    if (effectName.Contains("Resistance Down", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pct >= 75) return Tier.ExtraHigh;
                        if (pct >= 50) return Tier.High;
                        if (pct >= 30) return Tier.Mid;
                        return Tier.Low;
                    }

                    if (effectName.Contains("Damage Up", StringComparison.OrdinalIgnoreCase) || effectName.Contains("Damage Bonus", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pct >= 60) return Tier.ExtraHigh;
                        if (pct >= 40) return Tier.High;
                        if (pct >= 25) return Tier.Mid;
                        return Tier.Low;
                    }

                    if (effectName.Contains("DEF Down", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pct >= 60) return Tier.ExtraHigh;
                        if (pct >= 45) return Tier.High;
                        if (pct >= 30) return Tier.Mid;
                        return Tier.Low;
                    }

                    if (effectName.Contains("ATK Up", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pct >= 40) return Tier.ExtraHigh;
                        if (pct >= 30) return Tier.High;
                        if (pct >= 20) return Tier.Mid;
                        return Tier.Low;
                    }
                }
            }

            return Tier.None;
        }

        private static double GetRangeWeightMultiplier(WeaponInfo weapon, BattleContext ctx)
        {
            var range = weapon.AbilityRange?.Trim();
            if (string.IsNullOrWhiteSpace(range))
            {
                return 1.0;
            }

            var scenario = ctx.TargetScenario == EnemyTargetScenario.Unknown
                ? EnemyTargetScenario.SingleEnemy
                : ctx.TargetScenario;

            var isBuffLike = ProvidesHaste(weapon) ||
                             ProvidesWeaponBoost(weapon, ctx.PreferredDamageType) ||
                             ProvidesElementalWeaponBoost(weapon, ctx.EnemyWeakness) ||
                             ProvidesDamageReceivedUp(weapon, ctx.PreferredDamageType) ||
                             ProvidesSingleTargetDamageReceivedUp(weapon, ctx.PreferredDamageType) ||
                             ProvidesAllTargetDamageReceivedUp(weapon, ctx.PreferredDamageType) ||
                             ProvidesDamageTypeBoost(weapon, ctx.PreferredDamageType) ||
                             ProvidesDamageTypeDamageBonus(weapon, ctx.PreferredDamageType) ||
                             ProvidesDamageTypeAtkUp(weapon, ctx.PreferredDamageType) ||
                             ProvidesExploitWeakness(weapon) ||
                             ProvidesEnfeeble(weapon) ||
                             ProvidesEnliven(weapon) ||
                             ProvidesAtbConservationEffect(weapon, ctx.PreferredDamageType) ||
                             (ctx.PreferredDamageType != DamageType.Any && ProvidesAmpAbilities(weapon, ctx.PreferredDamageType));

            var isDebuffLike = ProvidesElementalResistanceDown(weapon, ctx.EnemyWeakness) ||
                               ProvidesDefenseDown(weapon, ctx.PreferredDamageType) ||
                               ProvidesDamageReceivedUp(weapon, ctx.PreferredDamageType) ||
                               ProvidesSingleTargetDamageReceivedUp(weapon, ctx.PreferredDamageType) ||
                               ProvidesAllTargetDamageReceivedUp(weapon, ctx.PreferredDamageType) ||
                               ProvidesTorpor(weapon);

            if (isBuffLike && !isDebuffLike)
            {
                if (range.Equals("All Allies", StringComparison.OrdinalIgnoreCase)) return 1.25;
                if (range.Equals("Single Ally", StringComparison.OrdinalIgnoreCase)) return 1.10;
                if (range.Equals("Self", StringComparison.OrdinalIgnoreCase)) return 0.95;
                return 1.0;
            }

            if (isDebuffLike && !isBuffLike)
            {
                if (range.Equals("All Enemies", StringComparison.OrdinalIgnoreCase))
                {
                    return scenario == EnemyTargetScenario.MultipleEnemies ? 1.30 : 1.20;
                }
                if (range.Equals("Single Enemy", StringComparison.OrdinalIgnoreCase))
                {
                    return scenario == EnemyTargetScenario.MultipleEnemies ? 0.95 : 1.00;
                }
                return 1.0;
            }

            // Mixed/unknown: keep it conservative.
            if (range.Equals("All Allies", StringComparison.OrdinalIgnoreCase) || range.Equals("All Enemies", StringComparison.OrdinalIgnoreCase)) return 1.05;
            if (range.Equals("Self", StringComparison.OrdinalIgnoreCase)) return 0.98;
            return 1.0;
        }

        public static string DescribeSynergy(WeaponInfo weapon, BattleContext ctx)
        {
            var reasons = new List<string>();

            if (ProvidesElementalResistanceDown(weapon, ctx.EnemyWeakness))
            {
                reasons.Add($"{ctx.EnemyWeakness} resistance down / weakness infliction");
            }

            if (ProvidesElementalDamageBonus(weapon, ctx.EnemyWeakness))
            {
                reasons.Add($"{ctx.EnemyWeakness} damage bonus");
            }

            if (ProvidesElementalDamageUp(weapon, ctx.EnemyWeakness))
            {
                reasons.Add($"{ctx.EnemyWeakness} damage up");
            }

            if (ProvidesWeaponBoost(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{ctx.PreferredDamageType} weapon boost");
            }

            if (ProvidesElementalWeaponBoost(weapon, ctx.EnemyWeakness))
            {
                reasons.Add($"{ctx.EnemyWeakness} weapon boost");
            }

            if (ProvidesDamageReceivedUp(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{ctx.PreferredDamageType} damage received up");
            }

            if (ProvidesSingleTargetDamageReceivedUp(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{ctx.PreferredDamageType} single-target damage received up");
            }

            if (ProvidesAllTargetDamageReceivedUp(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{ctx.PreferredDamageType} all-target damage received up");
            }

            if (ProvidesDamageTypeDamageBonus(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{ctx.PreferredDamageType} damage bonus");
            }

            if (ProvidesDamageTypeAtkUp(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{(ctx.PreferredDamageType == DamageType.Magical ? "MATK" : "PATK")} up");
            }

            if (ProvidesDefenseDown(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{(ctx.PreferredDamageType == DamageType.Magical ? "MDEF" : "PDEF")} down (lower weight)");
            }

            if (ProvidesExploitWeakness(weapon))
            {
                reasons.Add("Exploit Weakness");
            }

            if (ProvidesHaste(weapon))
            {
                reasons.Add("Haste");
            }

            if (ProvidesAtbConservationEffect(weapon, ctx.PreferredDamageType))
            {
                reasons.Add($"{ctx.PreferredDamageType} ATB conservation");
            }

            var atbPlus = GetAtbPlusAmount(weapon);
            if (atbPlus > 0)
            {
                reasons.Add($"ATB+{atbPlus}");
            }

            if (ProvidesEnfeeble(weapon))
            {
                reasons.Add("Enfeeble (lower weight)");
            }

            if (ProvidesEnliven(weapon))
            {
                reasons.Add("Enliven (raises damage buffs)");
            }

            if (ProvidesTorpor(weapon))
            {
                reasons.Add("Torpor (short burst damage vulnerability)");
            }

            if (ProvidesAppliedStatsDebuffTierIncreased(weapon))
            {
                reasons.Add("Applied debuff tier increased");
            }

            if (ProvidesAppliedStatsBuffTierIncreased(weapon))
            {
                reasons.Add("Applied buff tier increased");
            }

            if (ctx.PreferredDamageType != DamageType.Any && ProvidesAmpAbilities(weapon, ctx.PreferredDamageType))
            {
                reasons.Add("Amp abilities");
            }

            return string.Join(", ", reasons);
        }

        public static int CountSynergyMatches(string? effectTextBlob, BattleContext ctx)
        {
            return GetSynergyMatches(effectTextBlob, ctx).Count;
        }

        public static string DescribeSynergyMatches(string? effectTextBlob, BattleContext ctx)
        {
            var matches = GetSynergyMatches(effectTextBlob, ctx);
            return string.Join(", ", matches.Select(m => m.Reason));
        }

        private static List<Match> GetSynergyMatches(string? effectTextBlob, BattleContext ctx)
        {
            var matches = new List<Match>();

            if (ctx.EnemyWeakness != Element.None)
            {
                var elementName = ctx.EnemyWeakness.ToString();
                if (HasToken(effectTextBlob, $"{elementName} Resistance Down") || HasToken(effectTextBlob, $"Status Ailment: {elementName} Weakness"))
                    matches.Add(new Match("elem_res_down", $"{ctx.EnemyWeakness} resistance down / weakness infliction"));
                if (HasToken(effectTextBlob, $"{elementName} Damage Bonus"))
                    matches.Add(new Match("elem_dmg_bonus", $"{ctx.EnemyWeakness} damage bonus"));
                if (HasToken(effectTextBlob, $"{elementName} Damage Up"))
                    matches.Add(new Match("elem_dmg_up", $"{ctx.EnemyWeakness} damage up"));
                if (HasToken(effectTextBlob, $"{elementName} Weapon Boost"))
                    matches.Add(new Match("elem_weapon_boost", $"{ctx.EnemyWeakness} weapon boost"));
            }

            if (ctx.PreferredDamageType != DamageType.Any)
            {
                if (ctx.PreferredDamageType == DamageType.Physical)
                {
                    if (HasToken(effectTextBlob, "Phys. Weapon Boost")) matches.Add(new Match("phys_weapon_boost", "Physical weapon boost"));
                    if (HasToken(effectTextBlob, "Phys. Dmg. Rcvd. Up")) matches.Add(new Match("phys_rcvd_up", "Physical damage received up"));
                    if (HasAnyToken(effectTextBlob, "All-Tgt. Phys. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Phys. Dmg. Rcvd. Up"))
                        matches.Add(new Match("phys_rcvd_up_all", "Physical all-target damage received up"));
                    if (HasToken(effectTextBlob, "Phys. Damage Bonus")) matches.Add(new Match("phys_dmg_bonus", "Physical damage bonus"));
                    if (HasToken(effectTextBlob, "PATK Up")) matches.Add(new Match("patk_up", "PATK up"));
                    if (HasToken(effectTextBlob, "PDEF Down")) matches.Add(new Match("pdef_down", "PDEF down (lower weight)"));
                }
                else if (ctx.PreferredDamageType == DamageType.Magical)
                {
                    if (HasToken(effectTextBlob, "Mag. Weapon Boost")) matches.Add(new Match("mag_weapon_boost", "Magical weapon boost"));
                    if (HasToken(effectTextBlob, "Mag. Dmg. Rcvd. Up")) matches.Add(new Match("mag_rcvd_up", "Magical damage received up"));
                    if (HasAnyToken(effectTextBlob, "All-Tgt. Mag. Dmg. Rcvd. Up", "Status Ailment: All-Tgt. Mag. Dmg. Rcvd. Up"))
                        matches.Add(new Match("mag_rcvd_up_all", "Magical all-target damage received up"));
                    if (HasToken(effectTextBlob, "Mag. Damage Bonus")) matches.Add(new Match("mag_dmg_bonus", "Magical damage bonus"));
                    if (HasToken(effectTextBlob, "MATK Up")) matches.Add(new Match("matk_up", "MATK up"));
                    if (HasToken(effectTextBlob, "MDEF Down")) matches.Add(new Match("mdef_down", "MDEF down (lower weight)"));
                }
            }

            if (HasToken(effectTextBlob, "Exploit Weakness")) matches.Add(new Match("exploit_weakness", "Exploit Weakness"));
            if (HasToken(effectTextBlob, "Enfeeble") || HasToken(effectTextBlob, "Status Ailment: Enfeeble"))
                matches.Add(new Match("enfeeble", "Enfeeble (lower weight)"));
            if (HasToken(effectTextBlob, "Torpor") || HasToken(effectTextBlob, "Status Ailment: Torpor"))
                matches.Add(new Match("torpor", "Torpor (short burst damage vulnerability)"));
            if (ctx.PreferredDamageType == DamageType.Physical && HasToken(effectTextBlob, "Phys. ATB Conservation Effect"))
                matches.Add(new Match("phys_atb_conservation", "Physical ATB conservation"));
            if (ctx.PreferredDamageType == DamageType.Magical && HasToken(effectTextBlob, "Mag. ATB Conservation Effect"))
                matches.Add(new Match("mag_atb_conservation", "Magical ATB conservation"));
            if (HasToken(effectTextBlob, "Applied Stats Debuff Tier Increased"))
                matches.Add(new Match("applied_debuff_tier", "Applied debuff tier increased"));
            if (HasToken(effectTextBlob, "Applied Stats Buff Tier Increased"))
                matches.Add(new Match("applied_buff_tier", "Applied buff tier increased"));
            if (HasToken(effectTextBlob, "Amp Abilities"))
                matches.Add(new Match("amp_abilities", "Amp abilities"));

            // Dedupe by key so repeated tokens don't inflate count.
            return matches
                .GroupBy(m => m.Key)
                .Select(g => g.First())
                .ToList();
        }
    }
}
