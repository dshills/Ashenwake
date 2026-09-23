# First-hour guidance: opening route balance check

All five disciplines completed Act I and the optional Bell-Torn Pilgrim with fresh characters on seeds 42 and 43. Every route earned Pyrebound Treads, Heart of Serath, and exactly one Last Toll, completed the Widow's Crypt, and restored its final save with an identical state hash. No numerical balance changes were made from this check.

## Reproduction and scope

`OpeningGuidanceBalanceTests.EveryFreshDisciplineCanFinishActOneAndClaimOptionalPilgrimWithoutInjectedBuild` runs ten bounded routes through the public `EndgameRuntimeSession` command API. `CampaignRuntimeSmoke` supplies ordinary movement, loot, exploration, choice, and combat inputs; `RoamingChampionSmoke` handles the optional approach, challenge, fight, treasure, and return. The session is created normally at level 1. It receives no altered snapshots, injected equipment, damage boosts, invulnerability, or forced victory. All routes finish at level 3.

The campaign policy installs the already-owned Nerve of Ilyra and Orrun's Bone fragments and selects Stone Memory at Mara before entering Act I. It collects all ordinary loot and the crypt treasure. It does not equip newly acquired gear, allocate passive points, or temper equipment. The optional Pilgrim is challenged as soon as its source room is cleared: the monastery for seed 42, and the road for seed 43. Consequently, the two seeds test different encounter timing as well as different random outcomes; they are not controlled comparisons of champion power at an identical progression state.

Run from the repository root:

```sh
source tools/env.sh
dotnet test game/Ashenwake.Tests/Ashenwake.Tests.csproj --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --filter FullyQualifiedName~OpeningGuidanceBalanceTests --logger 'console;verbosity=detailed'
```

Detailed `OPENING_BALANCE` JSON records are emitted to test output. The initial milestone evidence is retained under `artifacts/opening-guidance/`: `balance.log`, `balance-results.json`, and `opening-balance.trx`.

## Measurements

All ten routes had **zero deaths and zero potion uses**. A combat tick is one simulation step begun with a living enemy. Simulation runs at 30 ticks per second; these durations exclude thinking, menus, reading, and human reaction time. Route commands include travel and loot actions. Health lost sums positive per-tick decreases; healing can make this exceed a single health bar. Room transitions are excluded from health measurements so full-health restores do not count as damage or healing.

| Discipline | Seed | Route commands | Combat ticks, entire route | Health lost | Lowest health | Pilgrim combat ticks | Pilgrim health lost | Bell Saint combat ticks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Vanguard | 42 | 2,115 | 1,515 | 74 | 90% | 204 | 33 | 856 |
| Vanguard | 43 | 2,096 | 1,532 | 53 | 95% | 153 | 13 | 889 |
| Veilwalker | 42 | 2,030 | 1,434 | 91 | 90% | 192 | 33 | 852 |
| Veilwalker | 43 | 1,982 | 1,419 | 110 | 89% | 163 | 33 | 855 |
| Arcanist | 42 | 1,380 | 765 | 32 | 96% | 129 | 0 | 379 |
| Arcanist | 43 | 1,536 | 994 | 45 | 95% | 119 | 0 | 610 |
| Gravecaller | 42 | 1,981 | 1,165 | 71 | 92% | 190 | 25 | 443 |
| Gravecaller | 43 | 2,052 | 1,280 | 90 | 96% | 162 | 23 | 513 |
| Warden | 42 | 2,231 | 1,614 | 77 | 90% | 194 | 33 | 971 |
| Warden | 43 | 2,282 | 1,679 | 57 | 92% | 151 | 0 | 1,078 |

The automated Pilgrim fights lasted 4.0–6.8 simulation seconds. The Bell Saint fights lasted 12.6–35.9 seconds. At least one actual bell-toll warning occurred in every Pilgrim route. These tests cover reachable, winnable encounters with published warning geometry; the separate `RoamingChampionCombatTests.BellWarningCanBeInterruptedAndDoesNotResolveAfterRestore` regression owns the targeted interrupt/cancel mechanic.

## Interpretation and remaining human checks

The routes expose no discipline-specific progression blocker, missing build reward, unavoidable death, or mandatory potion dependency. Warden took longest against the Bell Saint; Arcanist was fastest in this policy. Those differences are observations of a fixed skill-selection and movement policy, not a ranking of expert builds or a reason to flatten discipline identity.

The low health loss and short champion fights suggest that an experienced player may find the opening forgiving. The driver reads exact hazard geometry every tick, uses available skills and dodges promptly, and spends no time discovering controls. These runs therefore cannot establish that novice players will understand interrupts, survive the same fights, notice a secret, or finish an actual first hour. In particular, they do not show that players have enough time to read and apply a new hint during the Pilgrim's short fight.

Use the new optional guidance in independent first-time sessions before changing enemy damage or health. Record whether players can move and interact without coaching, identify and equip Pyrebound Treads, use the dodge trail, find Mara after earning Heart of Serath, recognize Bell Saint anchors, and either defeat or voluntarily retreat from the Pilgrim. Record discipline, encounter attempts, potions, time spent reading versus fighting, and dismissed hints. Keep the optional champion and hidden content outside mandatory progression.
