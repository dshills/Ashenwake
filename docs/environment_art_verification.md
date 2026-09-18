# Opening environment verification

This milestone replaces the solo opening's prototype grid and landmarks with authored procedural Greyhaven, Grey March road, monastery and Bell Saint sanctuary scenery. It changes presentation only. See `environment_art.md` for scope and reproduction instructions.

## Rendered opening

The final exported Mac application, using the development Mac's OpenGL Compatibility renderer, completed the real opening journey with 29 checks. Captures cover Greyhaven, the road, the monastery and all three Bell Saint phases. Inspection verified the workshop silhouettes and warm windows, funerary ruins, hanging bell, phase III broken chains, readable actors, persistent anchor labels and ground effects. Lighting was reduced after the first inspection to retain pale-stone and roof detail.

The route exercises actual map buttons and story confirmation, combat and independent replay. The additional environment checks verify scene replacement, cosmetic node types, ground below gameplay warnings, bounded palette counts, moving atmosphere, pause/resume, and the actual Reduced visual effects checkbox. Ground tops are below zero; the highest sanctuary inlay is Y=-0.0015 m. Recorded architecture uses 22 palette meshes in Greyhaven and 8–10 in the sampled Act I states; phase II additionally uses its emissive ritual material.

A separate normal-launcher rendered interaction route passed all 17 checks, including discipline selection, F at Mara, anatomy, map departure, NPC idle/pause and modal/focus behavior. One normal-launcher run emitted an ObjectDB warning at shutdown without identifying a leaked instance. Two isolated verbose repetitions passed with clean managed/native teardown and no warning. No reproducible environment resource defect was found; the original raw log is retained rather than treating the warning as fixed.

Standalone conservative bounds audits compile the actual art builders with recording helpers. Greyhaven's four current hub stages passed 1,198 transformed primitive checks at the actual 12×10 m room half-bounds. Act I passed 26,736 primitives across 144 combinations of bounds, encounters and boss phases. No above-ground scenery intersected the authoritative walkable rectangle. These audits are local diagnostic evidence, not new gameplay collision tests.

## Prism review

Prism reviewed the staged implementation through the approved Gemini provider with the repository rules. The final full review (`763a966a00e50ec6f1199e68322d4fab`) returned one high, four medium and two low findings. Findings were checked against the actual Godot version, scene layout and code paths; the raw report is preserved.

- The high batching finding recommends merging every obstacle into the room's common palette. Independent obstacle opacity requires separate material instances. The room contains two opening obstacles, each with four small palette meshes; the scenery and floor are already batched separately. Merging these obstacles would fade them together. This is an intentional, bounded exception documented in the art notes, not an unresolved high-severity rendering defect.
- The phase-change review correctly identifies an optimization opportunity: the sanctuary currently rebuilds its 10–11 palette meshes on two boss phase transitions. Removal and replacement occur synchronously in one callback, with no rendered frame between them; the asserted missing-frame flicker has no execution path. Build cost and future incremental updates remain profiling work rather than a hardware performance claim.
- The resource-churn recommendation is related to the same per-obstacle decision. Only the unit box is created in the builder constructor; cylinder and ring templates are created lazily when requested and disposed after merging. There is no per-frame geometry allocation. Broader immutable resource caching remains optional.
- The downward-beam quaternion concern was checked against the pinned Godot C# implementation. `new Quaternion(Vector3.Up, Vector3.Down)` returns a finite rotation, and the final 26,736-primitive bounds audit passes. The suggested `LookingAt(direction, Up)` would itself need a parallel-vector guard.
- Resolution-dependent fading and partial obstacle fading were addressed in a targeted follow-up: the radius is relative to viewport height and every part of an obstacle uses one common projected center. The original large/elongated-obstacle center heuristic remains a future limitation; current opening footprints are small and fixed.
- Color keys come from a finite set of lowercase six-digit literals in these builders. There is no user-supplied palette or mixed-prefix key path requiring normalization.

Earlier passes identified the phase III bell inscription offset and the builder lifecycle. Inscriptions now apply the bell's full rotation to both their positions and orientations; the single-use builder explicitly rejects calls after `Flush`. An unused-helper finding was false because `AdventureStage.AddMesh` still builds interaction markers and rings. One material recommendation referred to Unity's `MaterialPropertyBlock` and a nonexistent Server file; that API is not applicable to this Godot client.

The targeted follow-up review (`7a0bc75d6e338017b14367e9cc7939de`) returned one high, two medium and two low findings. Its asserted double translation is a false positive: `EnvironmentBuilder` bakes positions into vertices and creates mesh nodes at identity; there is no `mesh.Position = center` assignment in the changed construction path. The common center is local mesh-space data and receives the global transform exactly once. The proposed offset-only change would incorrectly test every obstacle at the room origin. The material cast/duplication findings also have no current path: both construction sites supply `StandardMaterial3D`, and the builder sets each committed surface material. Per-frame property assignment predates this pass; optimizing unchanged assignments remains optional. The AABB fallback is required for the original diagnostic arena, whose direct obstacle meshes do not have authored-room center entries. Its cached mesh-bound lookup is bounded by the small obstacle count.

Both high findings were manually rejected for these concrete code reasons; the raw counts are not presented as a clean automated approval. All actionable visual issues identified in review were fixed. The full reviewed implementation patch, targeted follow-up patch, raw reports and final source hashes are retained in local evidence. The verification record is written after review.

## Headless diagnostic correction

The first exported package passed the complete endgame route and its replay, then the new mote-animation assertion failed under the dummy renderer. A minimal Godot probe demonstrated that headless `MultiMesh.GetInstanceTransform` returns identity even after a nonidentity transform is set. The diagnostic now explicitly lists its two GPU transform-readback checks as skipped in headless runs. The real rendered journey still requires and passes all 29 checks; headless runs require 27. Reduced-effects visibility and fog checks run in both modes. This is a test-environment distinction, not a waived gameplay or animation failure.

## Final delivery

The solution builds without warnings, formatting verification passes, and the final macOS archive passed 96 UI checks (33 release, 17 interaction, 27 headless journey, 19 services) plus 170 character/model checks. The same exported application passed all 29 rendered journey checks with no skipped checks. Tests used fresh output directories and preserved player saves.

The 32,190-command campaign/endgame route independently replayed to `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. The 2,516-command Borrowed Memory route replayed to `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both hashes match the preceding gameplay/character milestone. Cooperative environment presentation is unchanged and no new cooperative visual claim is made.

The delivered `artifacts/environment-art/Ashenwake.zip` has SHA-256 `a5c5eae23cbd9295566e22304ea7efa6b4deedb1a6c1348ea9a5af6541f9e005`. Exact report and screenshot paths, source commit and hashes, reviewed patches and raw review dispositions are recorded in local `artifacts/environment-art/evidence.json`. The patch pair reconstructs every committed implementation file from the parent revision; this verification document is the only post-review addition.

Later regions retain prototype landmark art. Final textures, production LODs, measured hardware frame-time acceptance, and broader level-layout/art iteration remain future work.
