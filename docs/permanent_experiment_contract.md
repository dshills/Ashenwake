# Experiment runtime integration

The runtime wraps the existing endgame coordinator. UI, tooling, save buttons, and input adapters must route **every** command through the wrapper once a character opts in. Calling the nested mutable `Endgame` or `Production` session directly bypasses the wrapper's replay and cosmetic bookkeeping.

```csharp
ExperimentRuntimeSession.Create(combatJson, adventure, progression, campaign,
    endgame, experimentContent, seed: 42, discipline: "Vanguard", profile: profile);
ExperimentRuntimeSession.FromEndgame(combatJson, adventure, progression, campaign,
    endgame, experimentContent, validatedEndgameSnapshot); // hub only
ExperimentRuntimeSession.Restore(combatJson, adventure, progression, campaign,
    endgame, experimentContent, experimentSnapshot);
```

`ExperimentContent.Parse(json)` validates `content/experiments.json`; `Default()` gives the same maintained definition. `Hash` covers the immutable `Rules` object. `WithAdmission("Retired")` retains that hash and rejects only new borrowed contracts. The enclosing endgame combat catalog remains unchanged.

Read-only presentation uses `Endgame`, `Production`, `Combat`, `Content`, `View`, `Interactions`, `Tick`, `InHub`, `StateHash`, and `WorldEvents`. `View` contains name, admission status, `CanStart`, tradeoff/counterplay text, optional current `Run`, optional `Memory`, earned cosmetic IDs, and bounded `Entries` recording both choices and their authoritative outcomes. `Run` includes run/Sigil identity, status, source elite/room, bind/use ticks, and Echo action ID. `Memory` contains status, suppressed owned Mind ID, source actor/position, bind radius, `CanBind`, `CanRelease`, bound skill/lifetime, and the Storm warning/active field's position, radius, and countdown. The underlying combat view still accurately lists owned/equipped anatomy; presentation must overlay the suppression label from `Memory`.

Public actions:

```csharp
session.StartContract(sigilId, ExperimentChoice.BorrowMind);
session.StartContract(sigilId, ExperimentChoice.KeepMind);
session.BindMemory(sourceActorId);
session.ReleaseMemory();
session.Step(combatCommands);
session.ExecuteEndgame(endgameCommand); // also permanent/campaign commands nested here
session.Execute(experimentCommand, recordReplay: true);
```

`ExperimentCommand` carries `Action` (`Tick`, `StartContract`, `BindMemory`, `ReleaseMemory`, `Endgame`), `SigilId`, `SourceActorId`, `Choice`, optional `Commands`, and optional `Endgame`. Results contain `Success`, `Reason`, `CombatEvents`, and `WorldEvents`. Failed world actions roll back their complete joint snapshot. Tick inputs retain normal combat command rejection behavior; a rejected cast appears in `CombatEvents` without advancing the loan's usage receipt.

UI requirements:

1. At Greyhaven, show Borrow a memory beside the ordinary Sigil start, with the Mind suppression and self-risk stated before the action. Existing gate range and Sigil ownership remain authoritative. Retired content shows a closed-entry label and still permits ordinary runs.
2. At an offered memory, show its marker and bind range. The player explicitly binds it; there is no automatic pickup or auto-cast. Keep Release visible while the Mind is suppressed.
3. Show a borrowed Echo action using the existing `CastEcho` input, its remaining combat ticks, and the owned Mind suppression label. The single use triggers the ordinary authored ability.
4. Draw the Storm warning and active field using `Memory.HazardStage/Position/Radius/RemainingTicks`; a color-only signal is insufficient. The ordinary area view remains authoritative for active damage.
5. At actual eligible completion, display `cosmetic.borrowed_memory` from the wrapper's earned receipt. It has no power effect. Failed/abandoned/declined contracts display their actual outcome.
6. Save the joint wrapper archive to a new character destination. Loading an existing ordinary endgame save is an explicit hub import, not a silent overwrite of that source.

Persistence helpers:

- `ExperimentSaveStore.Read(combatJson, adventure, progression, campaign, endgame, experiment, json)`.
- `Write(path, same six content arguments, snapshot)` and `Load(path, same six content arguments)`; load returns `Session` and `RecoveredBackup`.
- `ImportEndgame(same six content arguments, originalJson)` validates the original endgame archive, requires a safe hub, and returns the wrapper without writing any file.
- `ProfilePath(path)` uses the existing reserved-name/symlink guard. Writes validate old archives/profile compatibility, merge profile metadata in memory, atomically publish character/valid previous backup first, and then union the separate profile under its writer lease. Failed post-character profile publication can safely retry.
- `ExperimentReplayRunner.Read(same content arguments, json)` and `Run(same content arguments, replay)` verify the rolling, bounded 1,800-command replay. The replay carries its recorded admission policy. Loading merged profile metadata establishes a fresh replay origin.

`ExperimentRuntimeSmoke.Next/Complete/MaximumCommands` provides an input-only route through fresh story/endgame or an imported hub. It selects BorrowMind, walks to/binds one actual elite memory, casts the real Echo, avoids the visible hazard, and finishes the actual Fracture before returning. With a bound memory in a cleared room, it may explicitly advance before the ability timer expires, leaving ground drops through the ordinary disclosed advance behavior. Tests and client smokes should also exercise decline, manual release, save while bound, death/retry, and retirement.

The five narrow integration hooks are the optional combat snapshot field, Mind effect filtering, ordinary cast/death/tick observations, room-transition cleanup, and raw optional-schema preflight in the endgame archive. All experiment implementation files otherwise live under `Core/Experiments`; no permanent item ownership or endgame reward calculation is duplicated.

CLI integration exposes `aw experiment validate|compile|demo [output]`, `aw experiment replay <path>`, and `aw experiment import-endgame <source> <new-destination>`. Validation and demo output include a key derived from every maintained content JSON file. `bash tools/experiment-verify.sh` builds tooling/client, verifies the real CLI route and save/replay, then runs the engine's `--echoes-smoke` route and verifies its completion, KeepMind, and Release branch replays. `tools/export.sh` compiles the experiment catalog and repeats the smoke in the packaged executable after the endgame and release checks.
