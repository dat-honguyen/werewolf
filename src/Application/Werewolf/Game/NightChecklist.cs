using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Game;

/// <summary>
/// The fixed sequence night roles act in (matches GetRulesEndpoint's NightActionOrder text).
/// <see cref="NightChecklist.CurrentStep"/> walks this order and returns the first step whose role
/// hasn't acted yet, skipping any step whose role has nobody alive to fill it (or, for Cupid, isn't
/// applicable past night 1). Each Submit/Use/Pass endpoint's Validate rejects the command unless
/// <see cref="NightChecklist.CurrentStep"/> matches its own step, so roles can no longer act out of
/// turn or before an earlier role has gone.
/// </summary>
public enum NightRoleStep
{
    Cupid,
    Werewolves,
    Doctor,
    Seer,
    Witch,
    Complete
}

public static class NightChecklist
{
    public static NightRoleStep CurrentStep(GameState state)
    {
        if (state.Phase != GamePhase.Night)
        {
            return NightRoleStep.Complete;
        }

        var cupidDone = state.NightNumber > 1 || !HasAliveRole(state, Role.Cupid) || state.CurrentNight.CupidDone || state.Lovers is not null;
        if (!cupidDone)
        {
            return NightRoleStep.Cupid;
        }

        var wolvesDone = !AlivePlayersWithRole(state, Role.Werewolf).Any() || state.CurrentNight.WerewolfLocked;
        if (!wolvesDone)
        {
            return NightRoleStep.Werewolves;
        }

        var doctorDone = !HasAliveRole(state, Role.Doctor) || state.CurrentNight.DoctorDone;
        if (!doctorDone)
        {
            return NightRoleStep.Doctor;
        }

        var seerDone = !HasAliveRole(state, Role.Seer) || state.CurrentNight.SeerDone;
        if (!seerDone)
        {
            return NightRoleStep.Seer;
        }

        var witchDone = !HasAliveRole(state, Role.Witch) || state.CurrentNight.WitchDone;
        if (!witchDone)
        {
            return NightRoleStep.Witch;
        }

        return NightRoleStep.Complete;
    }

    public static bool IsComplete(GameState state) =>
        state.Phase == GamePhase.Night && CurrentStep(state) == NightRoleStep.Complete;

    public static bool HasAliveRole(GameState state, Role role)
    {
        return state.Players.Values.Any(p => p.IsAlive && p.Role == role);
    }

    public static IEnumerable<Guid> AlivePlayersWithRole(GameState state, Role role)
    {
        return state.Players.Values.Where(p => p.IsAlive && p.Role == role).Select(p => p.PlayerId);
    }
}
