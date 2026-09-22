# Opening visual depth

The opening now has warm pools of light around Greyhaven's forge and lanterns, grave candles, crypt niches, monastery arches and the sanctuary's ritual candles. They illuminate nearby stone and wood while the existing regional lighting keeps the fighting floor readable. The light anchors follow the already authored fixtures; no new obstruction or interaction is introduced.

Paving has recessed edges, inset tops, varied wear and occasional broken corners. Sparse cracks, damp earth and moss collect toward the shoulders and ruined footings, leaving the central routes and boss arena quieter. Rock banks have varied shelves; ruined masonry and burial lids have restrained chips and scars. Geometry stays batched by material, and all ground detail remains below the gameplay floor.

Heroes and residents have folded capes and robe panels, defined hood rims, overlapping shoulder armor and shaped knee plates. Equipped chest capes use the same folded construction. Ghouls gain a stronger shoulder silhouette and burial rags; guards carry shaped cuirasses and coffin shields; archers have a folded mantle; the Bell Saint has cast ribs and ceremonial relief. These changes use the existing rig, equipment visibility and material palettes.

## Settings and limits

- **High:** up to six local lights per opening room (Greyhaven six, crypt five, road three, monastery and sanctuary two each).
- **Performance:** at most three, retaining the forge and both street lanterns in Greyhaven.
- **Reduced visual effects:** retains the light pools with no flicker. Normal fluctuation is limited to 5.5 percent and follows the game's pause state.

Local lights do not cast additional shadows or allocate shadow maps. They are room-owned nodes, reused when the room is unchanged and replaced when its style or bounds change. All motion is cosmetic, bounded and independent of the Core random streams. Later regions retain their existing environmental lighting; shared character improvements also appear outside the opening.

The pass uses the pinned Compatibility renderer and project-authored procedural geometry. It adds no external assets, downloads or rendering requirements. See [verification](visual_depth_verification.md) for native captures, geometry budgets, quality and lifecycle checks, and Prism review.
