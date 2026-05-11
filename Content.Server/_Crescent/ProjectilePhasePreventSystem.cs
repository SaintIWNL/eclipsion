using System.Numerics;
using Content.Shared._Crescent;
using Content.Shared.Projectiles;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

public sealed class ProjectilePhasePreventerSystem : EntitySystem
{
    [Dependency] private readonly PhysicsSystem _phys = default!;
    [Dependency] private readonly TransformSystem _trans = default!;
    [Dependency] private readonly ILogManager _logs = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    private readonly Dictionary<EntityUid, (ProjectilePhasePreventComponent Phase, ProjectileComponent Projectile)>
        _projectiles = new();

    private ISawmill _sawmill = default!;

    // xtra forgiveness beyond the projectile's exact movement distance. modify this if we ever raise tps opr have issues with phasing again
    private const float RaycastExtraDistance = 2f;

    // prevents tiny zero-length raycasts
    private const float MinimumTravelDistance = 0.001f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectilePhasePreventComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ProjectilePhasePreventComponent, ComponentShutdown>(OnShutdown);

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
        _sawmill = _logs.GetSawmill("Phase-Prevention");
    }

    private void OnStartup(EntityUid uid, ProjectilePhasePreventComponent comp, ref ComponentStartup args)
    {
        if (!TryComp<ProjectileComponent>(uid, out var projectile))
        {
            _sawmill.Error($"Tried to initialize ProjectilePhasePreventComponent on entity without ProjectileComponent. Prototype: {MetaData(uid).EntityPrototype?.ID}");
            RemComp<ProjectilePhasePreventComponent>(uid);
            return;
        }

        comp.start = _trans.GetWorldPosition(uid);
        comp.mapId = _trans.GetMapId(uid);

        if (projectile.Weapon != null &&
            _transformQuery.TryGetComponent(projectile.Weapon, out var weaponXform) &&
            weaponXform.GridUid != null)
        {
            comp.ignoredGrid = weaponXform.GridUid.Value;
        }

        _projectiles[uid] = (comp, projectile);
    }

    private void OnShutdown(EntityUid uid, ProjectilePhasePreventComponent comp, ref ComponentShutdown args)
    {
        _projectiles.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (owner, (phase, projectile)) in _projectiles)
        {
            if (TerminatingOrDeleted(owner))
                continue;

            if (!_physicsQuery.TryGetComponent(owner, out var bulletPhysics))
                continue;

            if (!_fixturesQuery.TryGetComponent(owner, out var bulletFixtures))
                continue;

            if (bulletFixtures.Fixtures.Count == 0)
                continue;

            var currentPos = _trans.GetWorldPosition(owner);
            var currentMap = _trans.GetMapId(owner);

            // Never raycast across maps
            if (currentMap != phase.mapId)
            {
                phase.start = currentPos;
                phase.mapId = currentMap;
                continue;
            }

            var previousPos = phase.start;
            var delta = currentPos - previousPos;
            var distance = delta.Length();

            if (distance <= MinimumTravelDistance)
                continue;

            var direction = delta / distance;

            string bulletFixtureKey = null!;
            foreach (var (key, _) in bulletFixtures.Fixtures)
            {
                bulletFixtureKey = key;
                break;
            }

            var ignoredGrid = phase.ignoredGrid;

            var ray = new CollisionRay(previousPos, direction, phase.relevantBitmasks);

            foreach (var hit in _phys.IntersectRay(
                         currentMap,
                         ray,
                         distance + RaycastExtraDistance,
                         projectile.Weapon,
                         false))
            {
                var hitEntity = hit.HitEntity;

                if (hitEntity == owner)
                    continue;

                if (projectile.IgnoreShooter && projectile.Shooter == hitEntity)
                    continue;

                if (projectile.IgnoredEntities.Contains(hitEntity))
                    continue;

                if (!_transformQuery.TryGetComponent(hitEntity, out var hitXform))
                    continue;

                if (projectile.IgnoreWeaponGrid &&
                    ignoredGrid != EntityUid.Invalid &&
                    hitXform.GridUid == ignoredGrid)
                {
                    continue;
                }

                if (!_physicsQuery.TryGetComponent(hitEntity, out _))
                    continue;

                if (!_fixturesQuery.TryGetComponent(hitEntity, out var targetFixtures))
                    continue;

                if (targetFixtures.Fixtures.Count == 0)
                    continue;

                string targetFixtureKey = null!;
                Fixture targetFixture = null!;
                foreach (var (key, value) in targetFixtures.Fixtures)
                {
                    targetFixtureKey = key;
                    targetFixture = value;
                    break;
                }

                var bulletEvent = new HullrotBulletHitEvent
                {
                    selfEntity = owner,
                    hitEntity = hitEntity,
                    selfFixtureKey = bulletFixtureKey,
                    targetFixture = targetFixture,
                    targetFixtureKey = targetFixtureKey,
                    selfPhys = bulletPhysics
                };

                try
                {
                    RaiseLocalEvent(owner, ref bulletEvent, true);
                }
                catch (Exception e)
                {
                    _sawmill.Error($"Failed to raise phase-prevent hit event: {e}");
                }

                break;
            }

            phase.start = currentPos;
            phase.mapId = currentMap;
        }
    }
}