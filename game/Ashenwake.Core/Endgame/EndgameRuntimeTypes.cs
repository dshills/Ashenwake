using System.Text.Json.Serialization;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Production;

namespace Ashenwake.Core.Endgame;

public enum EndgameRuntimeAction
{
    Tick, ClaimRecoverySigil, AttuneSigil, StartFracture, StartGodHunt,
    AdvanceEncounter, RetryEncounter, Abandon, ReturnToHub, Campaign, Production, EnableExplorationMap,
    StartRegionalHunt, TrackRegionalHuntClue, ClaimRegionalHuntReward, AbandonRegionalHunt, ReturnRegionalHunt,
    ResolveSecretClue, EnterSecretChamber, ChallengeSecretGuardian, ClaimSecretTreasure, ExitSecretChamber,
    EnterRoamingChampion, ChallengeRoamingChampion, ClaimRoamingChampionReward, ExitRoamingChampion
}

public sealed record EndgameRuntimeCommand(EndgameRuntimeAction Action, long SigilId = 0, string Id = "", string Value = "",
    CombatCommand[]? Commands = null, CampaignRuntimeCommand? Campaign = null, ProductionCommand? Production = null);
public sealed record EndgameRuntimeResult(bool Success, string Reason, CombatEvent[] CombatEvents, string[] WorldEvents);
public sealed record EndgameRuntimeFrame(EndgameRuntimeCommand Command, string StateHash, string EventHash);
public sealed record EndgameRuntimeSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "endgame-runtime.1";
    public long Tick { get; init; }
    public long OperationSequence { get; init; }
    public CampaignRuntimeSnapshot Campaign { get; init; } = null!;
    public EndgameState Endgame { get; init; } = null!;
    public CombatSnapshot? Combat { get; init; }
    public EndgameCombatManifest? Manifest { get; init; }
    public bool AwaitingRetry { get; init; }
    public bool EncounterCleared { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LocalMapAtlasState? ExplorationMap { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RegionalHuntState? RegionalHunts { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecretChamberState? SecretChambers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RoamingChampionState? RoamingChampions { get; init; }
}
public sealed record EndgameRuntimeReplay(int SchemaVersion, EndgameRuntimeSnapshot Initial, EndgameRuntimeFrame[] Frames);
public sealed record EndgameRunView(long Id, string Kind, string Name, string Region, int Tier, int EncounterIndex,
    int EncounterCount, int AttemptsRemaining, int Deaths, int RewardPercent, string Status,
    bool EncounterCleared, bool AwaitingRetry, string[] Rules, string[] Counterplay, string[] InheritedModifiers,
    int UncollectedDrops, bool CanAdvance, bool CanRetry, bool CanAbandon);
public sealed record FracturePreview(long SigilId, ulong Seed, string Region, int Tier, string BossFamily,
    string RewardTendency, int EncounterCount, int Attempts, string[] Rules, string[] Counterplay,
    string[] EncounterNames, string[] InheritedModifiers, string[] SkippedInheritance, string ManifestHash);
public sealed record EndgameRuntimeView(bool Unlocked, bool InHub, int HighestClearedTier,
    FractureSigil[] AvailableSigils, string[] UnlockedHunts, int Materials,
    IReadOnlyDictionary<string, int> Catalysts, bool CanClaimRecoverySigil, EndgameRunView? Run,
    int CompletedFractures, int CompletedGodHunts);
