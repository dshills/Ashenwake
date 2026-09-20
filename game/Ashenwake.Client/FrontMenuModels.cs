namespace Ashenwake.Client;

public sealed record FrontAbility(string Id, string Name, string Description, string ResourceCost);
public sealed record FrontDiscipline(string Id, string Resource, string Playstyle, string ResourceHint,
    CharacterAppearance Appearance, FrontAbility[] Abilities);
public sealed record CharacterSlot(string Filename, string Discipline, int Level, string Location, bool IsEchoes,
    bool Available, bool RecoveredBackup, string Notice, CharacterAppearance? Appearance, DateTime SavedUtc);
