# Visual Fractures and God Hunts

Press **B** to open the expedition board. Browse Sigil cards by region, tier, rules, boss family and reward tendency. Selecting a card only inspects it; its connected route previews three encounters and the final confrontation. Approach the Fracture gate in eastern Greyhaven, choose **Consume Sigil & enter**, and confirm the spend to start.

The **Hunts** tab presents the known gods with distinct emblems, required Fracture tiers, phase guidance and catalyst rewards. Locked known hunts can be inspected. The optional secret hunt's identity and mechanics remain hidden until unlocked. Hunt entry confirms its two-attempt commitment.

**Attunement** shows the current rule, a compatible replacement, their descriptions, the five-material fee and current balance. Previewing costs nothing. Apply at the gate to spend the materials and update the Sigil. The route shown above reflects the current Sigil until that change is committed.

**Run** shows completed/current/upcoming rooms or phases, remaining attempts, deaths, the current base material reward percentage and counterplay. Close the board to fight. After a defeat, Retry resets the current encounter while keeping cleared rooms. Abandonment asks for confirmation and preserves earned character progress, but grants no final expedition reward and does not refund the consumed Sigil.

Travel from an intermediate cleared room, or back to Greyhaven, asks before discarding uncollected ground drops. **Finish expedition** records the earned reward and keeps the final room's loot available until you return. Results name the actual material, mastery and catalyst award; these rewards are already recorded and require no extra claim. **Rewards** shows current balances and the last completed expedition.

The board pauses the world under its own `expedition-panel` owner and blocks world clicks, movement and combat keys. Closing it preserves manual and interruption pauses. B or Escape closes it; C, I and H close it before opening their destination menu. J switches to Journey outside an expedition and closes the board during one. Save/load remain available. Confirmation requests are invalidated by closing, hiding, changing tabs/selections, application focus loss, changed state or session restoration, and accepted requests can submit only once.

Core continues to own gate range, availability, material/Sigil spends, attempt counts, combat victory and permanent reward transactions. This update adds presentation and explicit commitment controls, with no new content, reward formulas, balance changes or save fields. Emblems and routes are drawn in Godot without bitmap assets or additional viewports.

See [verification and review evidence](expedition_board_verification.md).
