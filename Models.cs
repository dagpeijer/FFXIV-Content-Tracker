using System;
using System.Collections.Generic;

namespace ContentTracker;

public sealed class TrackerData
{
    public int SchemaVersion { get; set; } = 4;
    public List<CharacterRecord> Characters { get; set; } = new();
    public List<DutyRunRecord> Runs { get; set; } = new();
    public List<GilSessionRecord> GilSessions { get; set; } = new();
    public ActiveRunSnapshot? PendingRun { get; set; }
}

public sealed class CharacterRecord
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint HomeWorldId { get; set; }
    public string HomeWorldName { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(HomeWorldName)
        ? Name
        : $"{Name} @ {HomeWorldName}";
}

public sealed class EncounteredPlayerRecord
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(WorldName)
        ? Name
        : $"{Name} @ {WorldName}";
}

public sealed class DutyRunRecord
{
    public long Id { get; set; }
    public ulong CharacterContentId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public uint CharacterHomeWorldId { get; set; }
    public string CharacterHomeWorldName { get; set; } = string.Empty;

    public uint ContentFinderConditionId { get; set; }
    public uint TerritoryTypeId { get; set; }
    public string ContentNameEnglish { get; set; } = string.Empty;
    public string ContentNameGerman { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public long DurationSeconds { get; set; }

    public int? GilStart { get; set; }
    public int? GilEnd { get; set; }
    public int? GilDelta { get; set; }

    public bool DutyStarted { get; set; }
    public bool Completed { get; set; }
    public int WipeCount { get; set; }
    public string EndReason { get; set; } = string.Empty;
    public bool RecoveredSession { get; set; }
    public List<EncounteredPlayerRecord> Players { get; set; } = new();

    public string CharacterDisplayName => string.IsNullOrWhiteSpace(CharacterHomeWorldName)
        ? CharacterName
        : $"{CharacterName} @ {CharacterHomeWorldName}";
}

public sealed class GilSessionRecord
{
    public long Id { get; set; }
    public ulong CharacterContentId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public uint CharacterHomeWorldId { get; set; }
    public string CharacterHomeWorldName { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public long DurationSeconds { get; set; }

    public int? GilStart { get; set; }
    public int? GilEnd { get; set; }
    public int? GilDelta { get; set; }

    public string EndReason { get; set; } = string.Empty;

    public string CharacterDisplayName => string.IsNullOrWhiteSpace(CharacterHomeWorldName)
        ? CharacterName
        : $"{CharacterName} @ {CharacterHomeWorldName}";
}

public sealed class ActiveRunSnapshot
{
    public ulong CharacterContentId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public uint CharacterHomeWorldId { get; set; }
    public string CharacterHomeWorldName { get; set; } = string.Empty;
    public uint ContentFinderConditionId { get; set; }
    public uint TerritoryTypeId { get; set; }
    public string ContentNameEnglish { get; set; } = string.Empty;
    public string ContentNameGerman { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime LastSnapshotUtc { get; set; }
    public int? GilStart { get; set; }
    public int? LastKnownGil { get; set; }
    public bool DutyStarted { get; set; }
    public int WipeCount { get; set; }
    public bool RecoveredSession { get; set; }
    public List<EncounteredPlayerRecord> Players { get; set; } = new();
}
