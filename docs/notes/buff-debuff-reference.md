# Buff and Debuff Reference

This note collects player-facing in-game observations for buff/debuff scaling and special status behavior.

## Scope
- Tier tables below cover the buff/debuff families that are affected by buff/debuff extension.
- Values are separated by target side because the same named tier can scale differently on allies versus enemies.
- Entries marked as unreleased or uncertain are included as notes so they are not lost.

## Tier Values: Allies / Us

| Family | Buff tiers | Debuff tiers |
|---|---|---|
| Attack | `10%`, `20%`, `30%`, `40%`, `50%`, `60%`, `70%` | `-10%`, `-20%`, `-30%`, `-40%`, `-50%`, `-60%`, `-70%` |
| Defense | `20%`, `50%`, `80%`, `110%`, `150%`, `200%`, `250%` | `-15%`, `-30%`, `-45%`, `-60%`, `-75%`, `-85%`, `-95%` |
| Elemental Damage | `10%`, `25%`, `40%`, `60%`, `80%`, `100%`, `120%` | `-10%`, `-25%`, `-40%`, `-55%`, `-70%`, `-85%`, `-95%` |
| Elemental Resistance | `25%`, `37.5%`, `50%`, `62.5%`, `75%`, `85%`, `90%` | `-15%`, `-30%`, `-50%`, `-75%`, `-100%`, `-125%`, `-150%` |

## Tier Values: Enemies

| Family | Buff tiers | Debuff tiers |
|---|---|---|
| Attack | `15%`, `30%`, `50%`, `75%`, `100%`, `125%`, `150%` | `-10%`, `-20%`, `-30%`, `-40%`, `-50%`, `-60%` |
| Defense | `15%`, `30%`, `50%`, `75%`, `100%`, `125%`, `150%` | `-15%`, `-25%`, `-35%`, `-45%`, `-55%`, `-65%` |
| Elemental Damage | `15%`, `30%`, `50%`, `75%`, `100%`, `125%`, `150%` | `-10%`, `-25%`, `-40%`, `-55%`, `-70%`, `-80%` |
| Elemental Resistance | `20%`, `40%`, `60%`, `80%`, `100%`, `100%`, `100%` | `-15%`, `-30%`, `-50%`, `-75%`, `-100%`, `-125%` |
| Sigil Resistance | `1`, `2`, `3`, `4`, `5`, `6`, `7` | `-1`, `-2`, `-3`, `-4`, `-5`, `-6` |

## Status and Special-Case Notes

| Effect | Notes |
|---|---|
| Poison / Venom | `4%` max HP every `1.9s` on allies; `2%` max HP every `4.9s` on enemies |
| Sadness | `-50%` limit break / summon gain |
| Pain / Fog (wait-time effect) | Adds `3s` of waiting time on enemies |
| Confusion | `50%` chance; `10%` self-damage; damage type still uncertain; each self-hit appears to add `+25%` chance to end confusion |
| Sleep | `x2` damage received |
| Slow | `-50%` ATB gain |
| Block Rec. | `15s` max duration on enemies |
| Pain (HP damage-over-time effect) | `7.5%` max HP on allies; `1.5%` max HP on enemies |
| Agony | Same behavior as Pain, but doubled |
| Dread | `-50%`, `-60%`, or `-70%` tactics gauge gain rate based on number of affected characters; `-50%` in multiplayer |
| Enfeeble | `+2` debuff effect tiers on allies; `+1` debuff effect tier on enemies |
| Stop | `-25%` duration per application (minimum `-3s`); may still always apply for at least `1s` |
| Regeneration (non-heal) | `5%` max HP every `2.9s` |
| Regen | `15%` heal every `2.9s` |
| Haste | `+50%` ATB gain |
| Exploit Weakness | Damage buff on attacks that display the `Weakness` text |
| Enliven | `+2` buff effect tiers |
| Reflect | Unreleased |

## Caveats
- These values are currently maintained from in-game observation notes rather than datamined formula definitions.
- Where the note explicitly calls out uncertainty, the uncertainty is preserved here instead of being normalized away.
