# Death recaps and recovery

The solo campaign and endgame show a paused recap after defeat. It identifies the killing blow, recent incoming damage, harmful conditions active before that blow, and recovery consequences. The report is scrollable, including at 780×720; action buttons remain below it. Held attack/accept input must be released before those buttons activate.

**Continue from checkpoint** dismisses the campaign recap. The existing campaign rules have already restored the checkpoint; closing the report does not recover twice. Owned equipment, earned XP, materials and completed objectives remain. Unfinished encounters reset and an unfinished optional activity ends. The screen names the actual restored encounter. **Return to Greyhaven** uses the existing campaign travel action.

For Fractures and God Hunts, the recap shows the current encounter, completed-room count and remaining attempts after the death. **Retry encounter** uses the existing retry command and spends no additional attempt. When attempts are exhausted, the primary action becomes **Return to Greyhaven**. Closing the recap while awaiting recovery opens the expedition board, where the existing explicit abandonment flow remains available. The recap never silently abandons an active run.

## Damage contract

The engine-free combat session records actual health removed after mitigation and barrier absorption, capped at remaining health. The latest eight seconds of damage are retained, up to 64 hits. These numbers exclude prevented damage, absorbed damage and overkill. The fatal hit is preserved separately; it is not guessed from the last attacker visible after respawn. Entries include the source identity, originating attack, damage family and tick. Damage over time retains its originating attack plus the status effect identity. Missing source information is reported as unknown.

Conditions are the harmful statuses still active immediately before the fatal hit's later status/death processing. They are descriptive context, not a claim that each condition caused the death. Counterplay uses known attack mechanics where available and otherwise gives general timing guidance; endgame encounter guidance comes from the existing phase definition.

The recap is runtime-only. Same-encounter build projections and rejected-action rollback retain it, while new encounters start a fresh incoming-damage window. It changes no damage rules, combat events, serialized snapshots, content identities or replay hashes. It is not saved as character history. Loading a defeated expedition shows its authoritative recovery state and explicitly says the damage history is unavailable.

The panel owns a separate pause, clears pending combat input through the existing modal system, and releases only its own pause. Refreshing or reopening it grants no rewards and consumes no attempts. Switching characters or loading replaces the displayed history; going to the main menu hides it. Training uses its own report and never creates a journey death recap. Cooperative and independent diagnostic scenes are outside this UI milestone.

See [verification](death_recap_verification.md) for automated, native and Prism evidence.
