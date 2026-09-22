# Shattered Spine visual depth

Act IV's Bone Causeway, Contract Hall, Oathkeeper's Archive, Divine Memory and Covenant Warden court now have sculpted bone architecture, worn masonry and practical lighting.

- Curved, tapered ribs and vertebral collars give the bone buildings a stronger silhouette. Chipped stone and ivory, recessed tablet faces and deeper inscriptions add detail to the present-day courts.
- Broad beveled floor slabs retain the regional court layout. Dark mortar, sparse broken corners and settled bone dust frame the worn routes; decorative marks stay subdued beneath actual numbered fault warnings.
- Warm hearth light reaches the causeway facades. Witness lamps illuminate reading desks and the court perimeter. Divine Memory has a warmer golden palette, complete bone arches, clean ivory paving and intact inscriptions.
- Two folded banners move gently outside each room. Their upper and lower panels bend independently, with no per-frame mesh allocation.
- Covenant Warden gains layered kite shields, chipped tablet silhouettes, engraved medallions and a tapered bone frame. A steady law light follows its actual guard state and fades from its current brightness during the finite release after victory.

## Preferences and boundaries

High enables four environmental local lights; Performance retains the two at each room's main fixtures. Covenant Warden has one additional shadowless light in both settings, bringing its court to five and three. All new lights are steady and shadowless.

Pause freezes banner and boss animation. Reduced Effects settles the banners and active victory release even while paused, while retaining the real guard, oath and fault signals. Restoring effects cannot replay a victory. Existing reduced fog and ambient mote settings continue to apply.

All raised scenery, banners and fixtures stay outside the playable rectangle; the boss rig remains behind the north wall. Ground vertices stay below Y=0. These changes do not alter collision, routes, combat rules, saves or random streams. Room replacement releases owned moving meshes/materials without disposing shared surface textures.

## Inspecting the result

From Bash with a current exported macOS app:

```bash
source tools/env.sh
artifacts/export/macos/Ashenwake.app/Contents/MacOS/Ashenwake \
  --quit-after 24000 --log-file "$PWD/artifacts/spine-depth/manual/smoke.log" -- \
  --spine-smoke --capture-spine --output="$PWD/artifacts/spine-depth/manual"
python3 tools/check-godot-log.py artifacts/spine-depth/manual/smoke.log
```

The diagnostic reaches Act IV through real campaign commands, visits all five contexts, checks reversed fault warnings and Memory cleanup, completes the Warden and verifies replay. It also captures both quality settings and Reduced Effects and checks actual transformed geometry, bounded animation, preference changes and resource lifetime. See [verification results](spine_depth_verification.md).
