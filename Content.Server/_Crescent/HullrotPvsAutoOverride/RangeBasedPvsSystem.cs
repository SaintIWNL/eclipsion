using Content.Shared._Crescent.PvsAutoOverride;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Player;

namespace Content.Server._Crescent.HullrotPvsAutoOverride;

public sealed class RangeBasedPvsSystem : EntitySystem
{
    private const float ReconcileInterval = 0.2f;

    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly PvsOverrideSystem _override = default!;

    private float _accumulator;

    private readonly HashSet<(EntityUid Entity, ICommonSession Session)> _validPlayersScratch = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RangeBasedPvsComponent, ComponentRemove>(OnFree);
    }

    public void OnFree(Entity<RangeBasedPvsComponent> obj, ref ComponentRemove args)
    {
        foreach (var session in obj.Comp.SendingSessions)
        {
            _override.RemoveSessionOverride(obj.Owner, session);
        }
        obj.Comp.SendingSessions.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < ReconcileInterval)
            return;

        _accumulator -= ReconcileInterval;

        var playerData = _players.GetAllPlayerData();
        _validPlayersScratch.Clear();

        foreach (var player in playerData)
        {
            if (!_players.TryGetSessionById(player.UserId, out var session))
                continue;

            if (session.AttachedEntity is { } attached)
                _validPlayersScratch.Add((attached, session));
        }

        var enumerator = EntityManager.EntityQueryEnumerator<RangeBasedPvsComponent>();

        while (enumerator.MoveNext(out var uid, out var comp))
        {
            var pvsPos = _transform.GetWorldPosition(uid);
            var rangeSq = comp.PvsSendRange * comp.PvsSendRange;

            foreach (var (player, session) in _validPlayersScratch)
            {
                var delta = pvsPos - _transform.GetWorldPosition(player);
                if (delta.LengthSquared() > rangeSq)
                {
                    if (comp.SendingSessions.Remove(session))
                        _override.RemoveSessionOverride(uid, session);

                    continue;
                }

                if (comp.SendingSessions.Contains(session))
                    continue;

                comp.SendingSessions.Add(session);
                _override.AddSessionOverride(uid, session);
            }
        }
    }
}
