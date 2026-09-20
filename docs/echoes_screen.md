# Visual Echoes: Borrowed Memory

Press **H** or click **Echoes: Borrowed Memory** to open the board. Opening it pauses solo play. The **Contract** tab compares two choices beside one another:

- **Keep my Mind** retains the equipped Mind effect for an ordinary Fracture.
- **Borrow a memory** suppresses that effect while the loan is active. Defeat an elite, approach the offered memory, and bind one use of Echo Storm. The permanent fragment and Resonance remain intact.

Choose an owned Sigil, then use the corresponding entry button at the Fracture gate in Greyhaven. Both entry buttons disclose the Sigil consumption. Campaign, location, ownership and retired-entry gates remain authoritative. The card previews and tab/Sigil selection do not issue gameplay commands.

The right-hand memory display shows the source elite, loan state, owned Mind suppression/restoration, remaining combat time, and Bind/Cast/Release controls. Binding requires the actual proximity and action-readiness checks. Casting requires an available bound Echo. Buttons are disabled while paused. The timer reads simulation ticks, so opening a menu does not spend its lifetime. A separate label and countdown identify the hostile Storm warning and active field; move or dodge out of the marked circle. Expired, spent, released and lost memories cannot be cast.

The **Record** tab shows the earned Borrowed Memory cosmetic and actual contract history. Bind and cast the Echo, then complete that Fracture to earn the badge. It grants no additional power or currency. Keep, release, abandonment and completion records retain their real outcomes.

The **Character** tab identifies the active original or Echoes character. Save keeps Echoes progress separate. In Greyhaven, Save & Return preserves that progress and restores the original character. Continue first validates the saved Echoes character, then saves the current original before switching. Progress is not merged between characters. F5/F9 retain the existing save/restore controls for the active character.

The board uses native controls, scrollable content, decorative code-drawn seals and the shared skill icon. It fits 1280×800, 1000×720 and 780×720. The compact active-memory card sits below the reward feed and above the combat dock. No rules, content catalogs, archive schemas or combat rewards change in this presentation phase.

## Verification

The existing `--echoes-smoke` route continues to cover actual Fracture completion and exact gameplay replay. The new `--echoes-screen-smoke --discipline=Vanguard --output=<fresh-directory>` route drives the shipping director, native board controls and memory actions using an unchanged campaign-complete fixture. Add `--capture-echoes-screen` to capture rendered layouts. It checks the separate character lifecycle, release and expiration branches, Storm states and earned records. `tools/export.sh` runs both routes from the packaged game.
