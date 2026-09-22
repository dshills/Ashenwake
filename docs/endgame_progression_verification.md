# Earned campaign-to-endgame progression

The repeatable command is:

```bash
source tools/env.sh
dotnet run --project game/Ashenwake.Tooling --no-restore -- balance endgame artifacts/endgame-earned-example 3
```

Run it from the repository root in Bash. The destination must not exist. The optional seed count is 1–3, covering seeds 42–44 for every discipline. Each character starts a fresh campaign without shared profile data, completes all 15 main and eight optional encounters, then attempts Fracture tiers 1, 2 and 3 in order. No completed save, injected equipment, health, passive points or victories supply the measured route.

Use `--discipline Vanguard`, `Veilwalker`, `Arcanist`, `Gravecaller` or `Warden` to run a disjoint batch on a separate CPU process. Selection changes which fresh characters are measured, not the input policy. The selector and seed count are validated before creating the output directory. Separate batches can be checked and summarized with:

```bash
python3 tools/summarize-endgame-progression.py --output artifacts/endgame-earned-summary.json \
  artifacts/endgame-earned-vanguard artifacts/endgame-earned-veilwalker \
  artifacts/endgame-earned-arcanist artifacts/endgame-earned-gravecaller artifacts/endgame-earned-warden
```

The summary requires exactly the 15 unique discipline/seed pairs, matching policy/content identities, complete replay command intervals with their recorded frame counts and terminal hashes, matching save header hashes, and consistent room/tier receipts. It never combines or modifies character saves. A partial directory without `report.json` is not an accepted measurement.

## Build and input policy

`EndgameBalancePolicy` wraps the existing all-encounter `CampaignBalancePolicy(managedBuild: true)`. Campaign combat, route choices, starting anatomy and Stone Memory use its existing inputs. Before each Fracture, the same read-only `CampaignBalancePolicy.ManagedCommand` selector spends earned passive points alternating Offense/Defense and equips owned compatible strict raw-stat improvements at the appropriate Greyhaven service. The lower item ID resolves score ties. It does not move equipment from another occupied slot or displace an offhand for a two-handed item.

This shared selector avoids a different endgame build policy silently changing the comparison. It neither crafts, spends materials nor selects mutations. Endgame combat, warning avoidance, mechanisms, pickups and finite retries use `EndgameRuntimeSmoke`. The initial sigil is the ordinary recovery sigil; later sigils come from actual tier rewards. A failed expedition ends that measurement with its checkpoint and failure still visible, rather than replacing the run until it wins. After tier three, the route returns to Greyhaven and applies earned build upgrades once more.

The integrated runtime already extends the source progression catalog with resistance affixes through `EndgameProgression.Resolve`. The fresh campaign therefore uses the same managed input policy as the earlier campaign balance run, but the effective affix pool and composed combat catalog differ. Naturally rolled equipment and combat trajectories can differ before the endgame handoff. The report records both progression identities; this run is not presented as an exact replay or controlled before/after comparison with the standalone campaign catalog.

## Reports and verification

`policy.json` declares the effective policy hash, underlying campaign policy hash, composed combat identities, source and effective progression identities, campaign/adventure/endgame identities, seed count, units and limitations. Every discipline/seed directory contains:

- `report.json`: campaign room/act measurements; exact campaign handoff character and equipment; each tier-entry character and generated manifest; endgame room measurements; owned item grants; build changes; immutable room and final reward receipts; final progression and failed-run information.
- `segment-*.awendgame`: every public input in nonoverlapping replay segments of at most 900 commands. Each segment's frame count must match its command interval, its replay must reach the recorded state hash, and restoring its boundary must preserve that hash.
- `campaign-earned.save.json` and `final.save.json`: saves with profile companions, loaded and compared with the original state hashes. The handoff is evidence from this fresh run, not a reused input fixture.

Combat/loot/travel duration counts only simulation Tick inputs at 30 Hz. World/menu operations and rejected runtime operations are separate. The endgame runtime's outer Tick also increments for world commands, so it is deliberately not reported as elapsed combat time. Damage is the sum of player-targeted `DamageApplied`, and potions require a player potion-heal event. Clears, deaths, retries and committed room rewards are distinct metrics. Room identity stays with the outgoing room when its advance command grants experience or materials.

Experience is cumulative in `ProgressionSession`; leveling does not subtract or reset it. The aggregate verifier reconciles all recorded endgame experience gains with final XP minus handoff XP, including actual level-ups. Material earned/spent fields classify positive/negative wallet deltas per command. This policy has no material-spending actions, so the aggregate also requires zero spending and an exact reconciliation with the final wallet difference. Those fields would not distinguish gross simultaneous earning and spending in a different policy; no such claim is made here.

Campaign and Fracture metrics are separate because their runtime commands and room rewards differ. Hubs are present as their own rows and should be excluded from encounter clear-time comparisons. Tier-entry manifests disclose rule combinations, inheritance candidates, arena identities, pack composition and seeds. Comparing tiers therefore includes naturally different generated packs, rules, growing mastery, earned loot and level changes; it is not a controlled estimate of the tier multiplier alone.

## Limits

These are scripted public-input runs, not human encounter difficulty or enjoyment measurements. The raw-stat gear heuristic ignores legendary behavior powers and does not optimize each discipline. It does not test crafting or mutation strategies, God Hunts, or Fracture tiers 4–10. A zero-death result establishes that this declared naturally earned route can progress; it does not establish that every new player or every possible sigil can do so. Publicly earned save/replay evidence complements the isolated authored-loadout combat matrix.

## Focused checks

Six new `EndgameBalanceTests` and the five existing `CampaignBalanceTests` passed. They cover unchanged managed campaign inputs, read-only observation, actual fresh campaign-to-Fracture progression with bounded replay verification, incoming player event attribution, simulation-versus-menu timing, rejected operation duration, retries, clears and committed room rewards. Evidence is in `artifacts/endgame-combat-polish/earned-tests/earned-balance.trx` (10 cases) and `rejected-input.trx` (one added case).

Eight malformed selector/seed combinations failed before creating an output directory (`argument-validation.json`). The aggregate verifier rejected missing/duplicate discipline coverage and independently tampered XP/material row totals (`aggregate-validation.json`, `aggregate-delta-validation.json`); the source reports remained unchanged.

## Measured result

All **15 fresh routes** passed: all five disciplines, seeds 42–44, each completing the campaign and Fracture tiers 1–3. The accepted corpus contains **145,333 public commands** (102,935 campaign; 42,398 endgame), **788 persisted and replay-verified segments**, **180 committed Fracture rooms** and **45 final Fracture rewards**. There were **zero campaign deaths, endgame deaths or failed routes**, and zero endgame potion uses.

Every character entered endgame at level 10 with 5,150 XP and 475 materials, entered tier two and tier three at level 11, and finished at level 12 with 7,550 XP. The 2,400 earned XP per route reconciles exactly across both level-ups. Final materials were 575 or 585 according to the earned sigils' reward tendencies; every wallet delta reconciles and material spending remained zero.

The following values are per complete four-room Fracture, across 15 runs at each tier. They exclude Greyhaven travel, pickups and world/menu operations.

| Tier | Mean combat ticks (seconds) | Combat tick range | Mean incoming damage | Damage range | Deaths | Potions |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 585.9 (19.5 s) | 374–932 | 19.8 | 0–44 | 0 | 0 |
| 2 | 564.8 (18.8 s) | 353–902 | 41.7 | 0–168 | 0 | 0 |
| 3 | 650.2 (21.7 s) | 359–1,132 | 32.6 | 0–129 | 0 | 0 |

No blocking progression spike was observed for this declared earned-build route, so this milestone does not change numeric encounter balance. Tier-two damage is higher for some generated runs, but every discipline completed without consuming an attempt or a potion. Clear times are not a pure tier-multiplier comparison: region, pack/rule combinations and naturally earned build upgrades also vary. Human playtesting remains necessary before drawing difficulty conclusions.

Accepted aggregate evidence: `artifacts/endgame-combat-polish/earned-summary.json`. Its `sourceReports` list identifies every accepted per-run report. Vanguard's three completed reports are under `earned-progression`; that original all-discipline process was stopped after Vanguard completed to avoid duplicate work. The other disjoint batches are `earned-veilwalker`, `earned-arcanist`, `earned-gravecaller` and `earned-warden`. At most three measurement processes ran concurrently. The aggregate verified all 15 unique pairs against the same identities and independently reconciled their replay, save, receipt, XP and wallet accounting.

| Identity | SHA-256 |
| --- | --- |
| Earned endgame policy | `9E1B7408B85E01A1EA683864CE0C97EBA3814B0C246B244A800A7CBB4C8A2411` |
| Managed campaign policy | `CEFC5E8F35447C5E6E9ACFCAF0D8F2445E7CB4A56A16BE87017DFE59804E7E69` |
| Composed endgame combat | `33AA0EB02531A73A8441B616BEEF268ED7649E781EF5462716E1DAEC75C6F384` |
| Source progression | `8C52CF555D1007ECDEDECCD78FF3CE86111E4C3628BBC4A05DAC3DE19764117D` |
| Effective endgame progression | `BC580FBCC915960D31938C66E635C90F375ED92BDC026C8EDE3220C1BAC667C9` |
| Endgame policy content | `0CE6525E58EE70DEFA8417C386BF6A8CAA0DCC9E96F108060CBB48CF603FD0FC` |
