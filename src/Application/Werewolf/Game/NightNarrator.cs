using Application.Werewolf.Domain;

namespace Application.Werewolf.Game;

/// <summary>
/// Flavor text and role mapping for each <see cref="NightRoleStep"/>, used by
/// <c>Notifications/PlayerNotification.cs</c> to narrate whose turn it is without ever naming a
/// specific player (the room-wide broadcast) alongside a private "your turn" push to the actual
/// role holder(s).
/// </summary>
public static class NightNarrator
{
    public static string Prompt(NightRoleStep step) => step switch
    {
        NightRoleStep.Cupid => "Cupid, wake up and choose two players to fall in love.",
        NightRoleStep.Werewolves => "Werewolves, wake up and choose your victim.",
        NightRoleStep.Doctor => "Doctor, who will you save tonight?",
        NightRoleStep.Seer => "Seer, who would you like to inspect tonight?",
        NightRoleStep.Witch => "Witch, what will you do tonight?",
        _ => "The village falls silent as night ends."
    };

    public static Role? RoleFor(NightRoleStep step) => step switch
    {
        NightRoleStep.Cupid => Role.Cupid,
        NightRoleStep.Werewolves => Role.Werewolf,
        NightRoleStep.Doctor => Role.Doctor,
        NightRoleStep.Seer => Role.Seer,
        NightRoleStep.Witch => Role.Witch,
        _ => null
    };
}
