using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Werewolf.Domain;

public enum Role
{
    Villager = 0,
    Werewolf = 1,
    Seer = 2,
    Doctor = 3,
    Hunter = 4,
    Witch = 5,
    Cupid = 6,
    Tanner = 7
}

public enum LobbyStatus
{
    Open = 0,
    Starting = 1,
    Closed = 2,
    Cancelled = 3
}

public enum GamePhase
{
    RoleAssignment = 0,
    Night = 1,
    DayDiscussion = 2,
    DayVoting = 3,
    DayResolution = 4,
    GameOver = 5
}

public enum WinningFaction
{
    Villagers = 0,
    Werewolves = 1,
    Lovers = 2,
    Tanner = 3
}

public record GameSettings
{
    public required bool RevealRoleOnDeath { get; init; }
    public required bool DoctorCanSelfProtect { get; init; }
    public required bool WerewolfRequiresConsensus { get; init; }
    public required bool WerewolfCanTargetWerewolf { get; init; }
    public required bool WerewolfCanVoteNoKill { get; init; }
    public required bool WitchSinglePotionPerNight { get; init; }
    public required int MinPlayers { get; init; }
    public required bool AllowForceStart { get; init; }

    public static GameSettings Default() => new()
    {
        RevealRoleOnDeath = true,
        DoctorCanSelfProtect = false,
        WerewolfRequiresConsensus = true,
        WerewolfCanTargetWerewolf = false,
        WerewolfCanVoteNoKill = false,
        WitchSinglePotionPerNight = true,
        MinPlayers = 5,
        AllowForceStart = false
    };

    public static Dictionary<Role, int> DefaultRoleDistribution() => new()
    {
        [Role.Werewolf] = 1,
        [Role.Seer] = 1,
        [Role.Doctor] = 1,
        [Role.Witch] = 1,
        [Role.Hunter] = 1,
        [Role.Cupid] = 1
    };
}

[JsonConverter(typeof(RoomCodeJsonConverter))]
public readonly record struct RoomCode(string Value) : IParsable<RoomCode>
{
    private const int RequiredLength = 6;

    private static readonly HashSet<char> AllowedChars =
    [
        '2', '3', '4', '5', '6', '7', '8', '9',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H',
        'J', 'K', 'L', 'M', 'N', 'P', 'Q', 'R',
        'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z'
    ];

    public static RoomCode From(string value)
    {
        var normalized = Normalize(value);

        if (normalized.Length != RequiredLength || normalized.Any(c => !AllowedChars.Contains(c)))
        {
            throw new InvalidOperationException($"Invalid room code '{value}'.");
        }

        return new(normalized);
    }

    public static RoomCode Generate(Random random)
    {
        var source = AllowedChars.ToArray();
        var result = new StringBuilder(RequiredLength);

        for (var i = 0; i < RequiredLength; i++)
        {
            result.Append(source[random.Next(source.Length)]);
        }

        return new(result.ToString());
    }

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static RoomCode Parse(string s, IFormatProvider? provider) => From(s);

    public static bool TryParse(string? s, IFormatProvider? provider, out RoomCode result)
    {
        if (s is not null && Normalize(s).Length == RequiredLength && Normalize(s).All(AllowedChars.Contains))
        {
            result = new RoomCode(Normalize(s));
            return true;
        }

        result = default;
        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// Serializes RoomCode as a plain JSON string (e.g. "PQXR7K") instead of the default
/// { "value": "PQXR7K" } object shape, so it round-trips directly through HTTP request/response
/// bodies and route/query binding without a wrapper DTO.
/// </summary>
public sealed class RoomCodeJsonConverter : JsonConverter<RoomCode>
{
    public override RoomCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        RoomCode.From(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, RoomCode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
