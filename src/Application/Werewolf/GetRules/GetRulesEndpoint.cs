using Application.Werewolf.Domain;
using Application.Werewolf.GetRoles;
using System.Collections.Generic;

namespace Application.Werewolf.GetRules;

public record PhaseInfo
{
    public required GamePhase Phase { get; init; }
    public required string Description { get; init; }
}

public record SettingInfo
{
    public required string Name { get; init; }
    public required string Default { get; init; }
    public required string Description { get; init; }
}

public record RulesResponse
{
    public required string Overview { get; init; }
    public required List<PhaseInfo> Phases { get; init; }
    public required List<string> NightActionOrder { get; init; }
    public required List<string> WinConditions { get; init; }
    public required List<SettingInfo> Settings { get; init; }
    public required List<RoleInfo> Roles { get; init; }
}

// Reference endpoint describing the ruleset as actually implemented by the game engine (phases,
// win conditions, configurable settings, and roles) so a client can render rules text without
// hardcoding it, and so this document can be diffed against the code as behavior evolves.
public static class GetRulesEndpoint
{
    private const string Overview =
        "A social deduction game for 5+ players. The host creates a lobby, players join and ready " +
        "up, and the host starts the game once everyone is ready (or force-starts, if enabled). " +
        "Roles are assigned randomly per the lobby's role distribution. The game then alternates " +
        "Night and Day phases until a win condition is met.";

    private static readonly List<PhaseInfo> Phases =
    [
        new() { Phase = GamePhase.RoleAssignment, Description = "Game just started; roles have been assigned and night 1 begins immediately." },
        new() { Phase = GamePhase.Night, Description = "Living players with a night role (Cupid on night 1, Werewolf, Doctor, Seer, Witch) act. The phase auto-resolves into a death cascade once every living night role has acted." },
        new() { Phase = GamePhase.DayDiscussion, Description = "Night deaths are announced. No actions are available; the host explicitly advances to voting when discussion is over." },
        new() { Phase = GamePhase.DayVoting, Description = "Every living player casts one vote (or abstains). Voting auto-closes once all living players have voted, or the host can close it early." },
        new() { Phase = GamePhase.DayResolution, Description = "The player with the most votes is lynched (no lynch on a tie or if everyone abstains); resulting deaths resolve, then night falls again." },
        new() { Phase = GamePhase.GameOver, Description = "A win condition has been met. GameState.Result holds the winning faction and final roles." }
    ];

    private static readonly List<string> NightActionOrder =
    [
        "Cupid pairs two lovers (first night only, before any other action)",
        "Werewolves vote on a kill target; by default a target is mandatory and another werewolf can never be targeted, but WerewolfCanVoteNoKill/WerewolfCanTargetWerewolf can relax either rule (locks in once every living werewolf has voted). Living werewolves poll GET /api/v1/game/{roomCode}/werewolf/votes over HTTP (not SignalR) to see each other's votes and the lock as it happens",
        "Doctor protects one living player (never the same target as the immediately preceding night)",
        "Seer inspects one living player and learns only whether that player is a werewolf or not",
        "Witch heals the werewolves' locked target and/or poisons any living player, or passes. If WitchKnowsWerewolfTarget is on, she can call GET /api/v1/game/{roomCode}/witch/target first to learn who the werewolves locked onto before deciding",
        "Once every living night role has acted, the night resolves: the werewolf kill applies unless the target was healed or protected, the witch's poison target (if any) also dies, and any lover-link or Hunter-revenge chain deaths cascade from there",
        "This order is strictly enforced server-side: a role's Submit/Use/Pass endpoint rejects the call with 400 unless it is actually that role's turn (e.g. the Doctor can't act before the Werewolves have locked a target). Each transition also pushes a room-wide 'night.narration' SignalR broadcast (flavor text, never names a player) plus a private 'night.turn' push to whichever living player(s) hold the next role"
    ];

    private static readonly List<string> WinConditions =
    [
        "Lovers: if the two players Cupid paired are the last two players left alive, they win together regardless of their original factions. Checked first, ahead of every other condition.",
        "Tanner: if the village's day vote lynches the Tanner, the Tanner wins alone immediately — this pre-empts every other win condition and any lover-link/Hunter-revenge resolution the lynch would otherwise have triggered.",
        "Villagers: win once every werewolf has been eliminated (including if no players remain alive at all).",
        "Werewolves: win the instant living werewolves are greater than or equal to living non-werewolves."
    ];

    private static readonly List<SettingInfo> Settings =
    [
        new() { Name = "RevealRoleOnDeath", Default = "true", Description = "Whether a dead player's role is broadcast in live SignalR death notifications. Note: the debug GET game-state/log endpoints always show every role regardless of this setting." },
        new() { Name = "DoctorCanSelfProtect", Default = "true", Description = "Whether the Doctor may choose themselves as the night's protection target." },
        new() { Name = "WerewolfRequiresConsensus", Default = "true", Description = "If true, the werewolves' kill target only locks in when every living werewolf votes for the same player. If false, once all have voted, the target with the most votes locks in (ties broken by vote order)." },
        new() { Name = "WerewolfCanTargetWerewolf", Default = "false", Description = "If true, werewolves may vote to kill a fellow living werewolf (never themselves). If false (default), only non-werewolves are valid vote targets." },
        new() { Name = "WerewolfCanVoteNoKill", Default = "false", Description = "If true, a werewolf may cast a no-kill vote (omit the target) instead of naming a victim; if every/most werewolf votes lock in on no-kill, the pack kills no one that night. If false (default), a target is mandatory." },
        new() { Name = "WitchSinglePotionPerNight", Default = "true", Description = "If true, using either potion ends the Witch's turn for the night. If false, the Witch may use both the heal and poison potion on the same night." },
        new() { Name = "MinPlayers", Default = "5", Description = "Minimum number of joined players required to start the game." },
        new() { Name = "AllowForceStart", Default = "false", Description = "If true, the host may start the game before every player is marked ready." },
        new() { Name = "WitchKnowsWerewolfTarget", Default = "true", Description = "If true (the classic tabletop rule), the Witch can call GET /api/v1/game/{roomCode}/witch/target to learn who the werewolves locked onto before deciding whether to heal/poison/pass. If false, she decides blind." }
    ];

    [WolverineGet("/api/v1/rules")]
    public static RulesResponse Handle() => new()
    {
        Overview = Overview,
        Phases = Phases,
        NightActionOrder = NightActionOrder,
        WinConditions = WinConditions,
        Settings = Settings,
        Roles = GetRolesEndpoint.Handle()
    };
}
