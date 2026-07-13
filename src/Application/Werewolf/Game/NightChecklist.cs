using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Game;

public static class NightChecklist
{
    public static bool IsComplete(GameState state)
    {
        if (state.Phase != GamePhase.Night)
        {
            return false;
        }

        var hasAliveCupid = HasAliveRole(state, Role.Cupid);
        var hasAliveWolves = AlivePlayersWithRole(state, Role.Werewolf).Any();
        var hasAliveDoctor = HasAliveRole(state, Role.Doctor);
        var hasAliveSeer = HasAliveRole(state, Role.Seer);
        var hasAliveWitch = HasAliveRole(state, Role.Witch);

        var cupidDone = state.NightNumber > 1 || !hasAliveCupid || state.CurrentNight.CupidDone || state.Lovers is not null;
        var wolvesDone = !hasAliveWolves || state.CurrentNight.WerewolfLockedTarget is not null;
        var doctorDone = !hasAliveDoctor || state.CurrentNight.DoctorDone;
        var seerDone = !hasAliveSeer || state.CurrentNight.SeerDone;
        var witchDone = !hasAliveWitch || state.CurrentNight.WitchDone;

        return cupidDone && wolvesDone && doctorDone && seerDone && witchDone;
    }

    public static bool HasAliveRole(GameState state, Role role)
    {
        return state.Players.Values.Any(p => p.IsAlive && p.Role == role);
    }

    public static IEnumerable<Guid> AlivePlayersWithRole(GameState state, Role role)
    {
        return state.Players.Values.Where(p => p.IsAlive && p.Role == role).Select(p => p.PlayerId);
    }
}
