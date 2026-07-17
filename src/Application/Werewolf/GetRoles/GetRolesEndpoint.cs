using Application.Werewolf.Domain;
using System.Collections.Generic;

namespace Application.Werewolf.GetRoles;

public record RoleInfo
{
    public required Role Role { get; init; }
    public required string Faction { get; init; }
    public required string Description { get; init; }
}

public static class GetRolesEndpoint
{
    private static readonly List<RoleInfo> Roles =
    [
        new()
        {
            Role = Role.Villager,
            Faction = "Villagers",
            Description = "No special abilities and no night action. Counts toward the villager " +
                          "side for the werewolf win condition and votes during the day like " +
                          "everyone else. Wins when every werewolf is eliminated."
        },
        new()
        {
            Role = Role.Werewolf,
            Faction = "Werewolves",
            Description = "Each night, votes with the other living werewolves on a victim to kill. " +
                          "By default a target is mandatory and werewolves can never target another " +
                          "werewolf (no friendly fire) — both are configurable per game " +
                          "(WerewolfCanVoteNoKill lets the pack vote to kill no one; " +
                          "WerewolfCanTargetWerewolf lets them target one of their own, though never " +
                          "themselves). A doctor protection or the witch's heal potion can save the " +
                          "chosen target. The vote locks in once every living werewolf has voted, on " +
                          "whichever target got the most votes (ties broken at random). Every living " +
                          "werewolf privately sees each pack member's vote as " +
                          "it's cast, and the final locked-in target (or no-kill). Wins the instant " +
                          "the number of living werewolves is greater than or equal to the number of " +
                          "living non-werewolves."
        },
        new()
        {
            Role = Role.Seer,
            Faction = "Villagers",
            Description = "Each night, inspects one other living player and privately learns only " +
                          "whether that player is a werewolf or not a werewolf — never their exact " +
                          "role. Purely informational — has no effect on the game state."
        },
        new()
        {
            Role = Role.Doctor,
            Faction = "Villagers",
            Description = "Each night, chooses one living player to protect from the werewolves' " +
                          "kill (protecting the target that turn cancels the death). By default " +
                          "(DoctorCanSelfProtect=false) cannot target themselves; this is " +
                          "configurable per game. Cannot protect the same player on two consecutive " +
                          "nights — a different target (or the same target again after a night's " +
                          "gap) is always allowed."
        },
        new()
        {
            Role = Role.Witch,
            Faction = "Villagers",
            Description = "Holds one heal potion (once used, it always saves that night's " +
                          "werewolf-locked target — it cannot be used before the werewolves lock " +
                          "in) and one poison potion (kills any living player of choice), each " +
                          "usable once per game total, or may pass and act as a plain villager. By " +
                          "default (WitchSinglePotionPerNight=true) only one potion may be used per " +
                          "night; games can be configured to allow using both potions the same night."
        },
        new()
        {
            Role = Role.Hunter,
            Faction = "Villagers",
            Description = "When killed by any means (night kill, lynch, poison, or a chained " +
                          "lover-link death), the game pauses and the Hunter may immediately fire a " +
                          "revenge shot at one other living player, killing them too, or may " +
                          "decline; this can only trigger once per Hunter. All win conditions and " +
                          "the phase advance wait for this to resolve before continuing."
        },
        new()
        {
            Role = Role.Cupid,
            Faction = "Villagers",
            Description = "On the first night only, pairs any two living players (of any role, " +
                          "including werewolves or themselves) as lovers. If one lover dies by any " +
                          "means, the other dies immediately with them (which can itself chain into " +
                          "further lover-link or Hunter-revenge deaths). If the lovers are the last " +
                          "two players standing, they win together as a couple regardless of their " +
                          "original factions — this is checked before, and takes priority over, the " +
                          "werewolf/villager win conditions."
        },
        new()
        {
            Role = Role.Tanner,
            Faction = "Tanner",
            Description = "Has no night action and no other special ability — votes and appears as " +
                          "an ordinary villager for every other purpose, including counting as a " +
                          "non-werewolf for the werewolves' win condition. Wins alone, immediately, " +
                          "if and only if the village vote lynches them during the day; the game " +
                          "ends right then, before any lover-link chain death or Hunter-revenge " +
                          "pause that the lynch would otherwise have triggered. Dying by any other " +
                          "means (night kill, poison, or as a chained lover-link death) is just an " +
                          "ordinary death with no special win."
        }
    ];

    [WolverineGet("/api/v1/roles")]
    public static List<RoleInfo> Handle() => Roles;
}
