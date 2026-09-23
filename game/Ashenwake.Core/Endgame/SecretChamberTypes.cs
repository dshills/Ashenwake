using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record SecretClueDefinition(string Id, string Prompt, Position Position, string Hint, string[] Choices, string Solution);
public sealed record SecretChamberDefinition(string Id, string Name, int Act, string SourceEncounterId, string GuardianName,
    string PrimaryEnemyId, string EncounterId, string RewardItemId, string Description, string Counterplay,
    SecretClueDefinition[] Clues, Position EntrancePosition);
public static class SecretChamberCatalog
{
    public static readonly Position ExitPosition = new(-7000, 0), ChallengePosition = new(-2500, 0), TreasurePosition = new(4500, 0);
    public const int InteractionRange = 1800;
    public static IReadOnlyList<SecretChamberDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new SecretChamberDefinition("secret.belfry", "Unrung Belfry", 1, "campaign.monastery", "Last Tollkeeper",
            "enemy.funeral_guard", "secretarena.belfry", "item.griefs_reprieve",
            "A silent bell hangs over a procession whose last mourner never departed.",
            "Defeat the Tollkeeper before its Gravewake chant returns a fallen archer to the fight. Sidestep the announced sonic lanes.",
            [Clue("secret.belfry", 1, "Inspect the cracked hymn tablet", "The margin reads: Only silence admits the grieving.", "Keep silence", "Ring the chime"),
             Clue("secret.belfry", 2, "Examine the empty bell cradle", "A worn inscription says: Lay down the iron tongue; let no bell answer.", "Remove the clapper", "Strike the cradle"),
             Clue("secret.belfry", 3, "Inspect the mourner's seal", "Three kneeling figures face an unlit candle. The seal asks for an unspoken farewell.", "Bow in silence", "Speak the hymn")], new(8000, 6000)),
        new SecretChamberDefinition("secret.nest", "Hollow Nest", 2, "campaign.living_ruins", "Mother Without Eyes",
            "enemy.carnivorous_vine", "secretarena.nest", "item.widowthorn",
            "Beyond a living wall, a brood keeps feeding a mother who cannot see her children.",
            "Thin the needle swarm before the Mother can devour it to heal. Keep moving out of poison blooms and interrupt her venom pods.",
            [Clue("secret.nest", 1, "Study the blind root carving", "The roots recoil from torch soot. Beneath them: Darkness shelters the brood.", "Shade the roots", "Raise a flame"),
             Clue("secret.nest", 2, "Inspect the dry feeding bowl", "Fresh rain beads on a carved leaf. The bowl is marked: Give water, never blood.", "Pour clean water", "Offer blood"),
             Clue("secret.nest", 3, "Examine the sleeping seed", "The shell bears an open hand beneath loose earth: Bury gently; do not break.", "Cover with soil", "Crack the shell")], new(8000, 6000)),
        new SecretChamberDefinition("secret.furnace", "Cold Furnace", 3, "campaign.extraction_floor", "Ash-Eater",
            "enemy.forge_sentinel", "secretarena.furnace", "item.emberwake_mantle",
            "A decommissioned kiln still draws breath from the ashes of its workers.",
            "Interrupt the heat tender's bellows, cross away from announced storm links, and retreat from the emberling's death explosion.",
            [Clue("secret.furnace", 1, "Read the frost-covered gauge", "The maintenance plate reads: Shut fuel before touching the cooling line.", "Close the fuel valve", "Increase the fuel"),
             Clue("secret.furnace", 2, "Inspect the sealed coolant pipe", "The blue pipe is marked: Open only after the fuel falls silent.", "Open the coolant", "Seal the coolant"),
             Clue("secret.furnace", 3, "Read the cold pressure seal", "The gauge rests at zero. A final instruction reads: Vent the last breath before entry.", "Release the vent", "Relight the furnace")], new(8000, 6000))
    });
    private static SecretClueDefinition Clue(string id, int step, string prompt, string hint, string solution, string wrong)
        => new(id + ".puzzle." + step, prompt, step switch { 1 => new(-4200, -3500), 2 => new(4000, 1000), _ => new(8000, 6000) }, hint, step == 2 ? [wrong, solution] : [solution, wrong], solution);
    public static SecretChamberDefinition? Find(string id) => Definitions.FirstOrDefault(d => d.Id == id);
}
public sealed record SecretChamberActive(string Id, ulong Seed, string Stage);
public sealed record SecretChamberState
{
    public int SchemaVersion { get; init; } = 1;
    public long AttemptSequence { get; init; }
    public SortedDictionary<string, int> PuzzleProgress { get; init; } = new(StringComparer.Ordinal);
    public string[] Defeated { get; init; } = [];
    public string[] Claimed { get; init; } = [];
    public SecretChamberActive? Active { get; init; }
    public CombatSnapshot? Combat { get; init; }
}
public sealed record SecretClueView(string Id, string Prompt, string Hint, string[] Choices, bool InReach);
public sealed record SecretChamberEntryView(string Id, string Name, int Act, bool Known, bool Revealed, bool Claimed, int PuzzleStep, bool Here, SecretClueView? Clue, bool Defeated = false);
public sealed record SecretChamberRunView(string Id, string Name, string GuardianName, int Act, string Stage, string Counterplay, bool CanChallenge, bool CanClaim, bool CanExit);
public sealed record SecretChambersView(SecretChamberEntryView[] Entries, SecretChamberRunView? Run);
