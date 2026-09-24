# Loot, equipment and mix polish

Successful equipment changes have distinct metal, leather and cloth sounds. Removal uses a different movement from equipping. Material selection follows named equipment and the character's visible armor style. Both inventory drag-and-drop and inspection buttons acknowledge the completed equipment transaction. An incompatible release or rejected transaction gets one short sound; hovering, Escape, focus loss, cancelled drags, refreshing a panel and loading a save stay quiet.

Visible Rare and Relic drops have their own discovery cues. Legendary and Godwrought drops retain their established sounds and effects. Collected equipment has a short acknowledgement. Secret treasure sounds only after a successful claim; clues and unopened chambers remain quiet. Committed material rewards and champion rewards also receive feedback. Presentation receipts suppress duplicate events, and loaded inventory/reward state starts from a quiet baseline. Nearby repeated collection cues may coalesce rather than pile up.

Twelve original mono PCM16 cues are synthesized at 22,050 Hz with silent endpoints and a source peak at or below 0.42. The cache holds at most twelve streams. Each active presentation owner has at most two single-voice players; pickups cannot steal a more important treasure cue. Equipment uses the Interface slider; drops, collection and treasure use Effects. Scene/session replacement stops transient feedback. Existing regional scores, combat cues and gameplay RNG are unchanged.

## Quiet mode

Open **Settings → Audio → Quiet mode** to soften loud overlaps. It defaults off, persists with device preferences, and is reset by **Restore Audio defaults**. Old preferences load with Quiet mode off without rewriting their file. Independent volume and mute settings remain intact.

The Master bus has one optional compressor followed by one always-on peak limiter. Quiet mode uses a −18 dB threshold, 4:1 ratio, 500 µs attack, 180 ms release and no makeup gain. The limiter caps the pre-fader mix at −1 dB in both modes. Repeated scene loads do not stack effects. Existing critical-warning priorities and music ducking remain active.

## Verification

`--reward-audio-smoke --output=<fresh-directory>` checks reproducible distinct PCM, headroom/endpoints, routing, bounded voices, priority, coalescing, stop/reset behavior and native playback. It exports twelve audition WAVs. The package script includes its headless checks.

Appearance diagnostics exercise actual buttons, dragging and cancellation. Combat Feedback and Secret Chambers diagnostics verify earned loot, duplicate suppression, claims, restoration and replay. Settings diagnostics exercise the new control and preference recovery; native runs capture the actual Master DSP chain with overload, quiet-detail and representative busy-combat mixtures. Stress signals remain muted at the output during measurement. These are automated measurements, not a listening assessment or proof of balance on every speaker/headphone setup. See [verification evidence](reward_audio_verification.md).
