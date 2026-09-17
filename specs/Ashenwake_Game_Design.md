# Ashenwake
## Game Design Foundation

**Genre:** Isometric action RPG  
**Mode:** Single-player first; cooperative play as a later expansion  
**Tone:** Dark mythic fantasy: tragic, strange, beautiful, and occasionally human enough to be funny  
**Core fantasy:** Hunt monsters, descend into ruined sacred places, harvest fragments of dead gods, and rebuild yourself into something powerful enough to confront what killed them.

> **The gods did not abandon the world. They died in it.**

This document defines the game itself: world, story, player fantasy, combat, classes, progression, loot, enemies, campaign, and endgame. It deliberately avoids implementation and technology choices.

---

# 1. High Concept

Centuries ago, **the Ashenwake** tore the divine realm open and cast the bodies of the gods into **Edrath**. Mountains formed around their bones. Rivers changed course around divine blood. Forests grew from immortal flesh. Entire cities vanished beneath falling gods.

Death did not end divine power. Their remains radiate **Resonance**, an energy capable of altering matter, memory, life, and death. Wildlife mutates around it. Corpses awaken. Landscapes behave impossibly. Humans exposed for too long may become something else.

Humanity eventually learned to harvest that power. Fragments of divine bone, tissue, memory, organs, and even concepts can be implanted into living people. Survivors become **Wakebound**.

The player is an unusual Wakebound called **The Unbound**, capable of integrating fragments from several gods.

The central question is not merely *How powerful can I become?*

> **How much of myself am I willing to replace to become powerful enough?**

A build is therefore not merely equipment plus statistics. It is a deliberate physical and metaphysical reconstruction of the protagonist.

---

# 2. Design Pillars

## Combat must feel excellent before progression exists
A level-one character killing a basic enemy must already feel satisfying. Attacks require strong anticipation, impact, enemy reaction, sound, movement, and death feedback. Progression moves the player from vulnerable, to capable, to dominant, to spectacular.

## Builds change behavior, not merely numbers
`+7% damage` has a place, but memorable rewards change rules: burning enemies spread flame on critical hits; dodges leave shadows that repeat attacks; corpses become ammunition; standing still stores seismic force.

## Loot creates decisions
Upgrades should not collapse into one green number. Players trade reliability against burst, offense against survival, specialization against flexibility, immediate power against synergy, and human stability against divine transformation.

## The world rewards curiosity
Optional bosses, hidden chambers, strange NPCs, cursed shrines, rare encounters, secret materials, lore, and unusual fragments reward exploration. Some discoveries should make players say, “Wait, that's actually in the game?”

## Darkness needs contrast
Edrath contains horror and ruin, but also beauty, humor, friendship, stubborn communities, magnificent landscapes, and ordinary people getting on with life.

## Power has consequences
Divine integration changes appearance, abilities, NPC reactions, narrative options, and eventually the world. This is not a morality meter. Power should be tempting enough that accepting its cost is often reasonable.

---

# 3. Edrath and the Dead Pantheon

The campaign occurs roughly **430 years after the Ashenwake**. Civilization has recovered unevenly. People build homes against divine ribs, mine crystallized blood, farm soil altered by dead gods, and pray in temples constructed from the remains of beings once worshiped there.

Divine corpses are simultaneously holy sites, mines, weapons, scientific specimens, ecological disasters, pilgrimage destinations, and geopolitical resources.

## Vael — God of Flame and Creation
Fire, craft, invention, destruction, and renewal. Vael lies beneath the volcanic **Cinder Reach**. His fragments favor aggression, heat, explosions, forging, and power generated through destruction.

**Heart of Vael:** Critical strikes build Heat. At maximum Heat, the next major attack becomes Overheated, gaining substantial fire damage and igniting the surrounding area.

## Serath — Goddess of Death and Memory
Death, ancestors, remembrance, spirits, and the boundary beyond life. Her remains poison **The Mourning Vale**. Her fragments manipulate corpses, memory, curses, life drain, and spirits.

**Serath's Last Memory:** When an elite dies, capture an echo of one of its abilities for temporary use.

## Orrun — God of Stone and Oaths
Mountains, endurance, law, loyalty, and promises. His skeleton forms the **Shattered Spine**. His fragments favor armor, barriers, retaliation, stagger resistance, and seismic force.

**Orrun's Knuckle:** Standing still stores defensive and seismic power; movement releases the stored force.

## Ilyra — Goddess of Life and Hunger
Growth, fertility, animals, healing, adaptation, and consumption. Her corpse created **The Verdant Maw**, a jungle growing faster than humans can clear it. Her fragments favor poison, regeneration, beasts, mutation, and consumption.

## Nhal — God of Night and Secrets
Darkness, dreams, deception, forbidden knowledge, and hidden truth. **Nhal's body was never found.** His fragments involve shadow, teleportation, duplication, critical attacks, confusion, and impossible perception.

---

# 4. Central Mystery and Player

History teaches that the gods destroyed one another. History is wrong.

Something **killed the gods**.

Their deaths were sacrifices. The fallen bodies form metaphysical seals around Edrath, excluding something outside ordinary reality. Human civilization has spent four centuries mining those seals.

Civilization now depends on the same material whose extraction may cause the next apocalypse.

The game opens during an attack on **Greyhaven**. The protagonist is mortally wounded. **Mara Vey**, surgeon and forbidden divine anatomist, saves the character by implanting an unidentified fragment. It should be fatal. Instead, it integrates perfectly.

The protagonist can subsequently accept fragments from several divine lineages and becomes **The Unbound**.

Late in the campaign, the missing Nhal becomes the key. Nhal did not fall; **Nhal remained beyond the breach to hold it closed**. The protagonist's original implant contains not flesh but a fragment of Nhal's **identity**, explaining the player's unusual compatibility.

---

# 5. Starting Disciplines

The launch game has five disciplines. They establish early identity without permanently imprisoning the player in a class.

## Vanguard
Heavy melee using swords, axes, maces, shields, and great weapons. Themes: armor, stagger, retaliation, physical force, battlefield control.

**Momentum:** aggressive combat generates Momentum, spent to amplify heavy abilities.

## Veilwalker
Fast melee/ranged hybrid using daggers, short swords, bows, and hand crossbows. Themes: critical strikes, mobility, traps, shadow, precision.

**Exposure:** positioning and specific attacks reveal weaknesses that can be exploited.

## Arcanist
Manipulator of Resonance using staves, wands, focuses, and ritual blades. Themes: elemental attacks, barriers, chained effects, area control.

**Instability:** powerful spells raise Instability. High Instability increases power while introducing dangerous side effects.

## Gravecaller
Practitioner of forbidden death rites. Themes: minions, curses, corpses, spirits, life drain.

**Remains:** dead enemies become resources that can be consumed, animated, detonated, transformed, or harvested.

## Warden
Hunter and biological manipulator using bows, spears, axes, traps, beasts, and natural weapons. Themes: poison, bleeding, companions, regeneration, adaptation.

**Adaptation:** repeated exposure to threats temporarily evolves useful traits.

---

# 6. Skills and Mutations

The active loadout contains a primary attack, secondary attack, three combat/utility skills, one ultimate, dodge, and potion.

Skills gain **Mutations** that change behavior rather than merely percentages.

### Fire Lance
Base: launch a piercing projectile of flame.

- **Forking Flame:** splits after striking an enemy.
- **Furnace:** loses piercing and explodes violently on first impact.
- **Cauterize:** deals less direct damage but heals through burning damage.
- **Living Flame:** enemies killed by the skill may become temporary fire spirits.

### Shield Breaker
Base: Vanguard strike dealing heavy stagger.

- **Avalanche:** creates a seismic cone.
- **Executioner's Pace:** killing a staggered target resets the cooldown.
- **Orrun's Patience:** holding the attack charges a stronger blow and grants temporary armor.
- **No Ground Given:** converts the attack into a stationary counter.

Two players can therefore equip the same named ability while effectively playing different builds.

---

# 7. Divine Anatomy

Divine Fragments are Ashenwake's signature progression system. They include eyes, hearts, bone, nerves, crystallized blood, memories, organs, voices, oaths, shadows, and fragments of abstract divine concepts.

The player has specialized slots:

- **Mind:** spells, cooldowns, summons, perception, rule-changing effects.
- **Eyes:** targeting, critical mechanics, detection, range, weak points.
- **Heart:** resources, regeneration, major build engines, triggered effects.
- **Spine:** defense, movement, resistance, recovery, stagger.
- **Arms:** attacks, weapon effects, projectiles, physical skills.
- **Legs:** mobility, dodge, speed, terrain, movement triggers.

Fragments become exceptional through interaction.

**Eye of Vael:** Critical hits ignite enemies.  
**Heart of Serath:** Enemies dying from damage-over-time release temporary spirits.  
**Nerve of Ilyra:** Summoned creatures poison enemies they strike.

Nothing explicitly labels this a fire/spirit/poison build. The player discovers it.

Fragments carry **Resonance Tags** such as Flame, Death, Memory, Oath, Beast, Hunger, Shadow, Storm, or Void. Certain combinations unlock hidden interactions called **Concordances**.

---

# 8. Resonance and Manifestations

Installing fragments increases **Resonance**. Higher Resonance causes altered eyes, luminous veins, stone skin, burning fissures, spectral limbs, plant growth, doubled shadows, and other visible changes.

Crossing thresholds offers a choice among **Manifestations**, each combining an advantage with a complication.

### Burning Blood
Taking physical damage releases fire around the player. Healing potions restore less health.

### Whispering Shadow
Hidden enemies are automatically revealed. False enemies occasionally appear during combat.

### Stone Memory
Repeated damage from the same attack gains escalating resistance. Dodge recovery slows while resistance is active.

### Voracious Renewal
Consuming a corpse restores health and temporarily increases maximum life. Ordinary healing becomes less effective while corpses are nearby.

Resonance is not a good/evil meter. Low- and high-Resonance characters should both be viable.

---

# 9. Equipment, Loot, and Godwrought Items

Equipment slots: Head, Shoulders, Chest, Gloves, Belt, Legs, Boots, Amulet, two Rings, Main Hand, and Off Hand.

Rarity:

1. Common
2. Tempered
3. Rare
4. Relic
5. Legendary
6. Godwrought

Conventional affixes include attributes, critical chance, critical damage, attack speed, cooldown recovery, armor, resistance, resource generation, and movement.

Advanced affixes change mechanics: projectiles, chains, area, status duration, execute thresholds, barriers, corpse radius, companion behavior, dodge triggers, and resource conversion.

**Legendary items change rules. Godwrought items change stories.**

### Ashcleaver
An axe forged around a fragment of Vael's rib.

Initial: killing a burning enemy grants stacking attack speed.

Awakening: kill 1,000 burning enemies.

Awakened: at maximum stacks, attacks release waves of flame.

A later evolution might force a permanent choice: feed it Serath to make burning enemies resurrect as flaming revenants, or Orrun to convert its flame wave into a molten seismic attack.

Godwrought items become miniature progression stories rather than disposable stat sticks.

---

# 10. Combat, Damage, and Status

Combat emphasizes responsive movement, readable enemy attacks, positioning, crowd management, ability combinations, and build-driven destruction.

Normal enemies create rhythm. Specialists force target prioritization. Elites disrupt the player's routine. Bosses test understanding.

Damage families:

- **Physical:** Slash, Pierce, Crush
- **Fire**
- **Frost**
- **Storm**
- **Decay**
- **Venom**
- **Void**

Statuses include Burning, Bleeding, Poisoned, Chilled, Frozen, Shocked, Staggered, Cursed, Terrified, Marked, Vulnerable, and Rooted.

Statuses are resources as much as debuffs. Builds may stack, spread, consume, transform, detonate, or benefit from them.

A poison build might stack Venom for sustained damage, consume all stacks for burst, spread stacks on death, transform poison into healing, or cause poisoned enemies to spawn hostile plants.

The endgame should permit absurdly powerful interactions while preserving enough counterplay that the player operates the build rather than merely watches it.

---

# 11. Enemies, Elites, and Bosses

Enemy families contain simple roles that become interesting in combination.

### Example Cinder Pack
- **Ash Ghoul:** fast melee pressure.
- **Cinder Priest:** buffs allies and creates burning ground.
- **Furnace Brute:** slow armored frontal threat.
- **Emberling:** fragile creature that explodes when killed.

Elite modifiers include:

- **Mirrorborn:** creates copies.
- **Gravewake:** resurrects nearby dead.
- **Stormbound:** links enemies with damaging lightning.
- **Devourer:** consumes allies to heal and grow.
- **Null:** temporarily suppresses a Divine Fragment.
- **Hunter:** relentlessly pursues the player and resists control.
- **Martyr:** empowers surviving allies when killed.
- **Riftborn:** teleports through void breaches.

Higher difficulties combine modifiers, excluding combinations that become unreadable or effectively unavoidable.

## Boss Philosophy

Bosses are mechanics, not health bars. They require readable attacks, positional decisions, escalating phases, environmental interaction, and several viable responses.

### The Bell Saint
A priest fused into an enormous cathedral bell in the **Basilica of Last Mercy**.

**Phase One:** chains and sonic shockwaves.

**Phase Two:** ringing resurrects corpses while the player destroys ritual anchors.

**Phase Three:** the bell cracks open; the creature emerges while broken bell fragments continue ringing independently.

Rewards include a unique Serath Fragment and a chance for a Godwrought relic.

---

# 12. Campaign

## Act I — The Grey March
Frontier settlements, abandoned farms, battlefields, crypts, monasteries, and Greyhaven. The player learns fragment implantation and discovers evidence that official history of the Ashenwake is false.

## Act II — The Verdant Maw
A gigantic ecosystem growing from Ilyra's corpse: carnivorous plants, enormous insects, living ruins, altered communities, and biological horrors. The jungle behaves like a distributed organism and appears to be attempting to reconstruct Ilyra.

## Act III — The Cinder Reach
Volcanic territory around Vael: lava fields, obsidian cities, abandoned forges, fire cults, mines, and divine-industrial machinery. Humanity is extracting divine matter at industrial scale.

## Act IV — The Shattered Spine
Mountains built around Orrun's skeleton. Cities are carved directly into divine bone. Conflict centers on sacred law, broken kingdoms, giants, and ownership of Orrun's remains. Here the player learns that the divine corpses are seals.

## Act V — The Hollow Night
The search for Nhal leads beneath ordinary reality. Architecture repeats. Rooms remember previous visitors. Shadows act independently. Enemies sometimes appear before the event that creates them.

The revelation: Nhal never fell. Nhal remained beyond the breach to hold it closed.

The campaign ends with the player preventing immediate collapse but learning that the seals cannot simply be restored. Edrath must eventually solve the problem without relying on dead gods.

---

# 13. Factions and Characters

## Mara Vey
Divine anatomist who saves the protagonist. Brilliant, practical, ethically flexible, and genuinely concerned with preserving human life. Her flaw is believing that understanding something eventually justifies using it.

## The Reliquary Church
Publicly condemns harvesting divine remains while secretly maintaining one of the world's largest fragment collections. Many members sincerely believe unrestricted extraction will destroy civilization. Annoyingly, they may be right.

## The Anatomists
A loose network studying divine biology. Some are physicians and scholars. Others are the reason regulations exist.

## The Cinder Compact
City-states industrializing Vael's corpse. Extraction has produced wealth, heat, medicine, metallurgy, and improved living standards. Stopping it would hurt millions of ordinary people.

## Children of Ilyra
Communities within the Verdant Maw that embrace controlled biological transformation. They regard baseline humanity as an ancestral configuration rather than a sacred endpoint.

## The Oathbound
Orders dwelling within Orrun. Their magically binding contracts make their society stable and deeply uncomfortable. They believe civilization survives because promises have consequences.

## The Quiet
A secretive network devoted to Nhal. Members believe some truths are dangerous enough to deserve protection from discovery.

No major faction should be purely correct or cartoonishly evil.

---

# 14. Greyhaven and the Human Layer

Greyhaven becomes the primary hub. It begins damaged during the opening attack and is physically rebuilt as the campaign progresses. Rescued specialists, investments, and decisions alter the settlement.

Important residents can include:

- **Mara Vey:** Divine Anatomy and fragment research.
- **Torren Bale:** blacksmith and equipment modification.
- **Sister Cael:** former Reliquary priest and lore specialist.
- **Oris Fen:** eccentric cartographer obsessed with impossible spaces.
- **Kesh:** trader who claims to have sold merchandise to a god. Nobody believes him; he may be telling the truth.
- **The Pale Child:** an unsettling optional NPC who appears to know things that have not happened yet.

NPC dialogue changes in response to campaign events and visible Resonance. High divine transformation should sometimes inspire awe, fear, disgust, curiosity, or opportunism.

---

# 15. Crafting and Item Modification

Crafting should improve promising loot without turning the game into a spreadsheet with monsters attached.

Core services:

- **Tempering:** improve a conventional affix.
- **Rebinding:** replace one eligible affix.
- **Engraving:** add a limited rune-like modifier.
- **Extraction:** destroy a Legendary to preserve a special property.
- **Divine Grafting:** alter or evolve certain Godwrought items.
- **Purification:** reduce specific fragment complications at a meaningful cost.

Rare materials come primarily from playing the game—bosses, elite hunts, exploration, dismantling valuable equipment, and difficult encounters—not repetitive harvesting chores.

Perfect equipment should be difficult to create. Good equipment should not be.

---

# 16. Exploration and World Events

Campaign maps mix authored landmarks with variable encounter layouts.

Exploration events include:

- sealed tombs,
- collapsed divine anatomy,
- shrines with tradeoffs,
- roaming elite hunts,
- trapped travelers,
- faction skirmishes,
- cursed caravans,
- hidden Anatomist laboratories,
- unstable Resonance zones,
- optional mini-dungeons,
- rare merchants,
- memory echoes showing fragments of the pre-Ashenwake world.

### Resonance Storm
A regional event temporarily changes enemy populations, environmental hazards, loot, and fragment behavior.

### Divine Memory
The player enters a preserved memory belonging to a dead god. These short surreal encounters reveal lore while allowing mechanics impossible in ordinary Edrath.

### Wake Hunt
A named mutated creature roams a region until tracked and killed. Hunts have unique behaviors and rewards rather than merely increased statistics.

---

# 17. Progression

Progression has several parallel axes.

## Character Level
Provides baseline growth and access to skills and passive choices.

## Skill Mastery
Using and investing in skills unlocks Mutations and specialized enhancements.

## Divine Anatomy
Fragments create the largest build-defining changes.

## Equipment
Provides conventional progression plus Legendary rule changes.

## Godwrought Evolution
Long-form progression tied to particular treasured items.

## Greyhaven
The hub improves and unlocks services, stories, merchants, and optional content.

## Account-Wide Discovery
After completing the campaign, discovered crafting recipes, certain cosmetics, lore, and selected convenience unlocks can apply to future characters without eliminating the pleasure of starting over.

The game should avoid having fifteen currencies whose only purpose is to feed fifteen progression bars. Every progression system needs a distinct reason to exist.

---

# 18. Difficulty

The campaign begins with a broadly accessible difficulty curve. Higher difficulties should increase mechanical pressure, not simply inflate enemy health.

Difficulty can alter:

- enemy aggression,
- pack composition,
- elite modifier count,
- attack patterns,
- resistance interactions,
- environmental hazards,
- boss mechanics,
- rewards.

At high difficulty, enemies may acquire **Responses** to common player strategies. Shielded enemies might punish repeated ranged hits; some undead may benefit from careless corpse generation; certain Void enemies may follow teleporting players.

These counters should encourage adaptation without arbitrarily invalidating builds.

---

# 19. Death and Recovery

Death should matter without making experimentation miserable.

During the campaign, death returns the player to the latest anchor and resets nearby encounters. Bosses reset.

In high-level optional content, death can consume a limited number of attempts or reduce final rewards.

Ashenwake should not use corpse runs as a default punishment. The combat is fast, build experimentation is central, and making the player jog back to their trousers would add little besides cardiovascular fitness.

---

# 20. Endgame: The Fractures

After the campaign, unstable cracks called **Fractures** begin opening across Edrath as the weakened divine seals interact.

A Fracture is a configurable endgame expedition with increasing difficulty and unusual rules.

Examples:

- enemies burn but become faster while burning,
- healing creates hostile echoes,
- dead elites leave permanent hazards,
- the player's highest Resistance becomes a damage bonus but the lowest Resistance is further reduced,
- Divine Fragments periodically overcharge,
- bosses inherit one elite modifier from each preceding encounter.

Players collect **Fracture Sigils** that specify region, difficulty, modifiers, boss family, and reward tendencies.

The goal is controlled unpredictability rather than pure randomness.

---

# 21. Endgame: God Hunts

The most difficult encounters are **God Hunts**.

These do not fight the original gods. Instead, accumulated Resonance occasionally creates incomplete reconstructions—enormous entities attempting to become a dead god from scattered memories and biological material.

Examples:

- **The False Vael**
- **Ilyra Reborn in Teeth**
- **The Thousand Memories of Serath**
- **Orrun Without an Oath**

Each is mechanically elaborate and drops materials capable of evolving the strongest Godwrought items.

A final secret hunt can involve an entity attempting to reconstruct **Nhal**, creating a dangerous question: if the copy becomes perfect enough, is it still a copy?

---

# 22. Seasons Without Disposable Design

If seasonal content is eventually used, each season should introduce a meaningful experimental mechanic rather than a checklist.

Examples:

### Season of Echoes
Elite enemies can leave collectible memories whose abilities can temporarily be inserted into Divine Anatomy.

### Season of Broken Oaths
Players accept binding challenge contracts that modify a run in exchange for exceptional rewards.

### Season of Hunger
Ilyra's influence creates evolving biological equipment that feeds on specific enemy types.

Successful seasonal mechanics can later be refined into the permanent game. The game should not demand that players treat Ashenwake like a second job.

---

# 23. Narrative Choice

Choices should primarily express priorities and create consequences rather than produce a simplistic good/evil score.

Examples:

- preserve a divine extraction operation because a city depends on it, or shut it down because it damages a seal;
- give a dangerous Fragment to the Reliquary, Mara, a local community, or keep it;
- enforce an Oathbound contract that is lawful but cruel, or break it and damage the magical system sustaining the settlement;
- permit controlled Ilyran transformation to save a plague-stricken village.

Some consequences should appear much later.

The campaign ending can vary in alliances, surviving leaders, Greyhaven's condition, and the protagonist's relationship to Resonance, while preserving a common foundation for endgame content.

---

# 24. Art and Atmosphere Direction

The visual identity should be **mythic decay**, not generic medieval mud.

Divine remains create impossible scale. A road may pass through a rib cage larger than a cathedral. A city may hang from fossilized nerves. A forest may glow because its roots feed on divine blood.

Each god creates a distinct visual language:

- **Vael:** black glass, incandescent cracks, monumental forge geometry.
- **Serath:** pale stone, funeral cloth, memory apparitions, impossible reflections.
- **Orrun:** colossal bone-stone architecture, carved laws, severe geometry.
- **Ilyra:** lush beauty becoming anatomical on closer inspection.
- **Nhal:** absence, doubled shadows, impossible perspective, architecture that refuses to remain observed.

Violence should be impactful but readable. Visual effects must never become so dense that the player cannot understand the battlefield.

---

# 25. Sound and Music Direction

Music should combine orchestral dark fantasy with unusual textures tied to divine regions.

Vael favors percussion, metallic resonance, and low brass. Serath uses voices, distant bells, and incomplete melodies. Ilyra uses organic percussion, breath, wood, and unsettling natural sounds. Orrun uses deep drones, stone-like percussion, and ritual chanting. Nhal uses silence aggressively, reversed textures, missing beats, and sounds that seem spatially incorrect.

Combat audio must communicate mechanics. Important elite attacks, boss telegraphs, low-health states, cooldown events, and valuable drops need recognizable sound signatures.

A Godwrought item hitting the ground should sound like something the player immediately wants to inspect.

---

# 26. First Playable Slice

The first complete slice should prove the game, not the size of the design document.

It contains:

- Greyhaven as a small hub,
- one playable discipline,
- approximately six active skills,
- several Mutations,
- a small Divine Anatomy,
- roughly twenty meaningful equipment combinations,
- three normal enemy types,
- two specialist enemies,
- one elite system,
- one dungeon,
- one boss,
- one Godwrought item,
- one fragment interaction surprising enough to demonstrate the game's identity.

The slice ends with the player defeating the Bell Saint and acquiring a fragment that creates an obvious new build possibility.

If replaying that dungeon with the new build is immediately appealing, Ashenwake has its foundation.

If it is not fun, the answer is not more lore, more acts, more crafting currencies, or a larger map. Fix combat and builds first.

---

# 27. What Ashenwake Should Avoid

Ashenwake should deliberately resist several common ARPG traps:

- Loot where 99.9% is meaningless.
- Legendary items that are merely larger numbers.
- Skill trees filled with `+2%` nodes.
- Mandatory daily chores.
- Excessive currencies.
- Enemies whose difficulty is primarily enormous health.
- Effects so visually dense that mechanics disappear.
- A campaign treated as an inconvenient tutorial for endgame.
- Respec systems designed to punish experimentation.
- Seasonal mechanics built primarily around fear of missing out.
- Lore that exists only in collectible text dumps.
- A protagonist who becomes a demigod while NPCs continue asking them to kill six rats.

The game should respect the player's time and intelligence.

---

# 28. The Desired Player Story

A strong Ashenwake session should generate stories such as:

> “I found a Serath heart that releases spirits when poisoned enemies die. I was already running an Ilyra poison Warden, so I swapped one Mutation to spread poison on companion hits. Then I found a Vael ring that makes summoned creatures explode when they expire. The whole build suddenly became this chain reaction where poisoned enemies die, spawn spirits, the spirits poison more enemies, then explode.”

That sentence is the game.

The campaign, world, art, bosses, and lore provide meaning and atmosphere. But the long-term engine of player fascination is discovering systems that interact in ways that feel clever, powerful, and personally assembled.

---

# 29. Identity Statement

**Ashenwake is an action RPG about building power from the remains of dead gods.**

Its distinguishing elements are:

1. **Divine Anatomy** makes the character's body part of the build.
2. **Fragments interact systemically**, encouraging discovery rather than prescribed sets.
3. **Resonance makes power visible and consequential.**
4. **Godwrought equipment evolves through use and player decisions.**
5. **The world itself is built physically and culturally around dead gods.**
6. **The central narrative conflict makes the loot fantasy part of the problem.**

The player spends the game harvesting divine power to save a civilization that may be destroying itself by harvesting divine power.

That contradiction is Ashenwake.
