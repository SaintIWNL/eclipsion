using System.Numerics;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Projectiles;

namespace Content.Server._Crescent.ProximityFuse;

public sealed class ProximityFuseSystem : EntitySystem
{
    private const float DiscoveryInterval = 0.08f;

    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<ProximityFuseTargetComponent> _targetQuery;

    private readonly List<EntityUid> _keysScratch = new();

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _targetQuery = GetEntityQuery<ProximityFuseTargetComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ProximityFuseComponent, ProjectileComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var projectile, out var xform))
        {
            if (!_xformQuery.TryGetComponent(projectile.Shooter, out var shooterTransform))
                continue;

            if (comp.Safety > 0)
            {
                comp.Safety -= frameTime;
                continue;
            }

            var ourMapPos = _transform.ToMapCoordinates(xform.Coordinates).Position;
            var maxRangeSq = comp.MaxRange * comp.MaxRange;

            if (!ProcessTrackedTargets(uid, comp, ourMapPos, shooterTransform, maxRangeSq))
                continue;

            comp.NextDiscoveryScan -= frameTime;
            if (comp.NextDiscoveryScan > 0)
                continue;

            comp.NextDiscoveryScan = DiscoveryInterval;

            var nearby = _lookup.GetEntitiesInRange(uid, comp.MaxRange, LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var near in nearby)
            {
                if (!_targetQuery.HasComponent(near))
                    continue;

                if (!_xformQuery.TryGetComponent(near, out var txform))
                    continue;

                if (shooterTransform.GridUid == txform.GridUid)
                    continue;

                if (comp.Targets.ContainsKey(near))
                    continue;

                var distance = Vector2.Distance(ourMapPos, _transform.ToMapCoordinates(txform.Coordinates).Position);

                if (distance <= comp.MaxRange)
                    comp.Targets[near] = distance;
            }
        }
    }

    private bool ProcessTrackedTargets(
        EntityUid uid,
        ProximityFuseComponent comp,
        Vector2 ourMapPos,
        TransformComponent shooterTransform,
        float maxRangeSq)
    {
        _keysScratch.Clear();
        _keysScratch.AddRange(comp.Targets.Keys);

        foreach (var near in _keysScratch)
        {
            if (!_xformQuery.TryGetComponent(near, out var txform))
            {
                comp.Targets.Remove(near);
                continue;
            }

            if (shooterTransform.GridUid == txform.GridUid)
            {
                comp.Targets.Remove(near);
                continue;
            }

            var distance = Vector2.Distance(ourMapPos, _transform.ToMapCoordinates(txform.Coordinates).Position);

            if (distance * distance > maxRangeSq)
            {
                comp.Targets.Remove(near);
                continue;
            }

            if (comp.Targets.TryGetValue(near, out var lastDistance))
            {
                comp.Targets[near] = distance;
                if (distance > lastDistance)
                {
                    Detonate(uid);
                    return false;
                }
            }
            else
            {
                comp.Targets[near] = distance;
            }
        }

        return true;
    }

    public void Detonate(EntityUid uid)
    {
        if (HasComp<ExplosiveComponent>(uid))
            _explosion.TriggerExplosive(uid);
        else
            QueueDel(uid); 
    }
}