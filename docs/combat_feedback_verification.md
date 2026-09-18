# Combat feedback verification

This presentation milestone adds discipline and monster attacks, dodge/hit/death clips, bounded transient effects, synthesized combat sounds, and persistent Bell Saint phase/defeat animation. Core rules and content are unchanged. See `combat_feedback.md` for scope and reproduction.

## Validation

The solution builds without warnings and the existing Core suite passes all 312 tests. The rendered feedback diagnostic passes 171 checks, including three captures. The headless version passes 168 structural/audio checks. Coverage includes all five disciplines, monster windup/recovery, terminal deaths, pause, unchanged actor roots, material isolation, bounded effect resources, all 16 PCM cues, and bell transitions.

The opening journey sends real Core events through the production presentation adapter. It verifies attacks, damage, delayed falls, boss defeat, persistent hazard anticipation, and actual hazard-resolution attacks. Same-arena Core projections retain valid feedback; replacing the arena rejects the completed old arena's actor cues. A fatal hit replaces its damage number with FALLEN or SILENCED. Active hazard labels do not advertise a recovery window before the warned strike resolves.

The final source headless journey passes 42 checks. It explicitly records three skipped rendered-only assertions: two ambient MultiMesh transform readbacks and floating death text. The dummy renderer does not return real MultiMesh transforms, and production intentionally does not create floating feedback labels headlessly. These assertions remain required in rendered runs. An export verification was stopped before the affected diagnostic when the unguarded floating-label assertion was discovered; the corrected diagnostic passed in both environments before the final package run.

One initial rendered journey failed because the diagnostic used encounter completion alone to trigger the bell defeat sequence. The boss can die while summoned enemies remain. It now uses the same authoritative predicate as production: dead Bell Saint or cleared encounter. Raw initial outputs are retained.

## Prism

Prism used the approved Gemini provider and repository rules. The staged implementation review (`23b4d1ce6665d0af20119c19b8bd9f36`) reported zero high and two medium findings:

- The service-mat Flush finding is false: `AdventureStage.AddServiceMats` already ends with `builder.Flush()`. Its existing call was outside the changed hunk.
- The visibility finding describes an intentional scope boundary. Core's `ActorVisible` represents concealment/proximity, not death; visible enemy corpses remain in the authoritative view and the rendered journey verifies their full falls. Hidden actors stay hidden, removed/expired summons disappear, and a departed arena clears its presentation. The suggested unconditional visibility override would reveal hidden actors. Documentation now states this boundary explicitly.

The hazard-label follow-up (`6127e8faa3691dbecfc745538ba97bed`) reported zero high, two medium and two low findings:

- Per-actor hazard lookup and existing label construction are bounded presentation work. Core caps campaign hazards at 32. Avoiding repeated scans and iterator/string allocations is a valid future profiling optimization, not evidence of a measured performance failure. No hardware frame-time claim is made.
- Recover is assigned by Core as soon as a hazard caster has no pending ability, even while its warned strike remains outstanding. No combat damage/vulnerability rule depends on that display-state string. The warning is prioritized until resolution; genuine shield/vulnerability mechanic labels are retained. Showing RECOVERY WINDOW during that live attack would contradict the sustained anticipation pose. Real-event regression coverage verifies the chosen behavior.
- Repeated role/name string work largely predates this milestone; broader label caching remains optional profiling work.

The isolated headless-label correction review (`50a887e0661c557b6c76340480585b13`) reported one high, one medium and one low finding. All were manually assessed rather than presenting this as a clean automated approval:

- The high screenshot concern is false: `JourneySmoke.Capture` returns immediately when headless, before viewport access. Headless journey execution passes with a clean log.
- The proposed headless text assertion cannot pass because `Sandbox.Feedback` intentionally returns before creating labels in that environment. The rendered test requires the actual visible FALLEN label; the headless report names this skip. Other event, pose, effect, stale-batch and death assertions still run headlessly.
- A recursive tree inspection once at the first diagnostic kill is bounded test-only work, not a production per-frame traversal.

The raw reports, reviewed patches, build/test logs, captures and archive hashes are retained under local `artifacts/combat-feedback`. No manually confirmed blocking review findings remain. Review checks also led to fixes for source-owned hazard event routing and sustained anticipation, targetless facing, stale events after respawn, and death text replacing damage text.

## Final package

The delivered Mac archive passed 449 packaged headless checks: 33 release/UI, 17 interaction, 42 opening journey, 19 services, 170 model/resource, and 168 combat-feedback checks. The same exported binary passed 45 rendered opening checks with 15 captures and 171 rendered feedback checks with three captures. All final Godot logs are clean. Inspection confirmed the warning label, attack poses, falling enemies, SILENCED boss feedback, and bell chain-break framing.

The 32,190-command campaign/endgame route independently replayed to `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. The 2,516-command Borrowed Memory route replayed to `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both match the preceding environment milestone. The same archive passed the actual two-player WebSocket route with 1,011 matching snapshots, zero mismatches and ten reward receipts.

`artifacts/combat-feedback/Ashenwake.zip` has SHA-256 `2dca589f7b3f41f509d9ac1961d2efa80af945416c6289d96be4fe22bf8433ed`. Local `artifacts/combat-feedback/evidence.json` records the source commit, reviewed patches, report/capture paths and source/archive hashes. Applying the three reviewed patches to the parent source reconstructs every committed implementation file; this verification record is the only post-review addition.

## Limits

This is a procedural solo combat presentation pass, focused on the opening through Bell Saint. Shared co-op rigs gain anticipation/recovery changes; network event-driven clips/audio/effects remain future work. Production skinning, recorded sound, independent playtests and hardware performance acceptance remain open. No player saves are used by the diagnostics.
