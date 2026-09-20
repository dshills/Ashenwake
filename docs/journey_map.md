# Journey map and journal

Press **J** to open the Journey screen. The connected map shows Greyhaven and all five campaign regions, with separate current, unlocked, cleared and locked states. Click a location to inspect it; selection never travels. Choose its explicit travel action to depart. Locked regions remain inspectable and explain their unlock requirement.

The current objective stays beside the map. Regional details show completed encounters, the next encounter's guidance and authored completion reward, and undiscovered later encounters. Current-region actions lead onward, resolve a story decision, return to Greyhaven or enter available optional exploration. Greyhaven lists nearby residents and the first Divine Anatomy reward invitation. Ground equipment remains separate from earned boss fragments.

Travel that would leave ground loot opens a confirmation with the destination and number of drops. Cancel to remain and collect them, or confirm to depart. Changing selection or tabs, closing the screen, changing authoritative state, losing application focus or loading a session invalidates the confirmation. Story decisions retain their separate permanent-choice confirmation and use the same stale-request protection. Returning from combat and exploration keeps the existing Core rules.

The **Journal** has four categories:

- **Objectives:** the current objective and completed regional encounters, with a link back to the route or pending decision.
- **Discoveries:** visited regions and earned exploration discoveries. Regional testimony appears only after its revealing encounter has been completed.
- **Choices:** decisions actually reached, the committed outcome, and a general reminder when consequences remain pending. Unrevealed future outcomes are not listed.
- **People:** rescued Greyhaven residents and their current reactions.

Map, Story and Journal pause the world under their own `journey-panel` owner. Anatomy retains its existing independent owner. Closing the screen preserves other pause reasons. Escape closes it; C, I, B and H close it before opening their destination screen; J toggles Journey. Save/load and keyboard UI navigation remain available. The backdrop blocks world clicks and scrolling while the screen is open.

The map and its region landmarks are drawn directly in Godot with retained native buttons, without textures or additional viewports. Presentation reads existing campaign/combat views and dispatches existing director events. There are no new progression rules, rewards, save fields or balance changes. See [verification and review evidence](journey_map_verification.md).
