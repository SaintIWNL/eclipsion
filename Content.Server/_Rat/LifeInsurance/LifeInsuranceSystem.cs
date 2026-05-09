using System.Linq;
using Content.Server._Rat.LifeInsurance.Components;
using Content.Server.Bank;
using Content.Server.GameTicking;
using Content.Server.Materials;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._Rat.LifeInsurance;
using Content.Shared.Access.Systems;
using Content.Shared.Ghost;
using Content.Shared.Materials;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.GameTicking;
using Content.Shared._Shitmed.Body.Organ;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Rat.LifeInsurance;

public sealed class LifeInsuranceSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly SharedGhostSystem _ghost = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    private TimeSpan _nextGhostSync = TimeSpan.Zero;

    public override void Initialize()
    {
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, MaterialAmountChangedEvent>(OnMaterialsChanged);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceSelectTargetMessage>(OnSelectTarget);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceVoidInsuranceMessage>(OnVoidInsurance);
        SubscribeLocalEvent<LifeInsuranceConsoleComponent, LifeInsuranceEjectProteinsMessage>(OnEjectProteins);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MindComponent, MindGotRemovedEvent>(OnMindGotRemovedFromContainer);
        SubscribeLocalEvent<GhostComponent, ComponentStartup>(OnGhostStartup);
        SubscribeLocalEvent<GhostComponent, MindAddedMessage>(OnGhostMindAdded);

        SubscribeNetworkEvent<GhostInsuranceRespawnRequest>(OnInsuranceRespawnRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextGhostSync)
            return;

        _nextGhostSync = _timing.CurTime + TimeSpan.FromSeconds(1);
        SyncGhostInsuranceButtons();
    }

    private void OnUiOpened(EntityUid uid, LifeInsuranceConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnMaterialsChanged(EntityUid uid, LifeInsuranceConsoleComponent component, ref MaterialAmountChangedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnSelectTarget(EntityUid uid, LifeInsuranceConsoleComponent component, LifeInsuranceSelectTargetMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!HasConsoleAccess(uid, user))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-access-denied"), uid, user, PopupType.Small);
            return;
        }

        var target = GetEntity(args.Target);
        if (target == EntityUid.Invalid || !_mind.TryGetMind(target, out var targetMindId, out _))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-invalid-target"), uid, user, PopupType.Small);
            return;
        }

        var life = EnsureComp<LifeInsuranceComponent>(targetMindId);

        if (TryComp<MobStateComponent>(target, out var targetMob))
        {
            if (targetMob.CurrentState != MobState.Alive)
            {
                _popup.PopupEntity(Loc.GetString("life-insurance-popup-target-not-alive"), uid, user, PopupType.Small);
                return;
            }
        }

        if (life.IsInsured)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-already-insured"), uid, user, PopupType.Small);
            return;
        }

        if (life.PendingRespawnAt != null)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-pending-respawn"), uid, user, PopupType.Small);
            return;
        }

        if (!TryComp<MaterialStorageComponent>(uid, out var materialStorage))
            return;

        if (_materialStorage.GetMaterialAmount(uid, component.ProteinMaterialId, materialStorage) < component.RequiredProteins)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-insufficient-proteins"), uid, user, PopupType.Small);
            return;
        }

        var nextPrice = GetCurrentPrice(component.BaseRequiredCredits, life.RespawnCount);

        if (!_bank.TryBankWithdraw(user, nextPrice))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-insufficient-credits"), uid, user, PopupType.Small);
            return;
        }

        if (!_materialStorage.TryChangeMaterialAmount(uid, component.ProteinMaterialId, -component.RequiredProteins, materialStorage))
        {
            _bank.TryBankDeposit(user, nextPrice);
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-insufficient-proteins"), uid, user, PopupType.Small);
            return;
        }

        life.IsInsured = true;
        Dirty(targetMindId, life);
        UpdateUi(uid, component);
    }

    private void OnVoidInsurance(EntityUid uid, LifeInsuranceConsoleComponent component, LifeInsuranceVoidInsuranceMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!HasConsoleAccess(uid, user))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-access-denied"), uid, user, PopupType.Small);
            return;
        }

        var target = GetEntity(args.Target);
        if (target == EntityUid.Invalid || !_mind.TryGetMind(target, out var targetMindId, out var mind))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-invalid-target"), uid, user, PopupType.Small);
            return;
        }

        if (mind.CurrentEntity != target)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-invalid-target"), uid, user, PopupType.Small);
            return;
        }

        var life = EnsureComp<LifeInsuranceComponent>(targetMindId);

        var hasCoverage = life.IsInsured || life.PendingRespawnAt != null;
        if (!hasCoverage)
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-nothing-to-void"), uid, user, PopupType.Small);
            return;
        }

        if (life.IsInsured)
            life.IsInsured = false;

        if (life.PendingRespawnAt != null)
        {
            life.PendingRespawnAt = null;
            life.PendingRespawnJob = null;
            life.PendingRespawnStation = null;

            if (TryComp<GhostComponent>(target, out var ghost))
                _ghost.SetInsuranceRespawnData(target, false, TimeSpan.Zero, ghost);

            PushInsuranceStatusToMind(targetMindId, false, TimeSpan.Zero);
        }

        Dirty(targetMindId, life);
        _popup.PopupEntity(Loc.GetString("life-insurance-popup-void-success"), uid, user, PopupType.Small);
        UpdateUi(uid, component);
    }

    private void OnEjectProteins(EntityUid uid, LifeInsuranceConsoleComponent component, LifeInsuranceEjectProteinsMessage args)
    {
        if (args.Actor is not { Valid: true } user)
            return;

        if (!HasConsoleAccess(uid, user))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-popup-access-denied"), uid, user, PopupType.Small);
            return;
        }

        _materialStorage.EjectMaterial(uid, component.ProteinMaterialId);
        UpdateUi(uid, component);
    }

    private bool HasConsoleAccess(EntityUid console, EntityUid user)
    {
        if (!_accessReader.GetMainAccessReader(console, out var reader))
            return true;

        return _accessReader.IsAllowed(user, console, reader);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (!_mind.TryGetMind(args.Target, out var mindId, out _))
            return;

        TryActivateInsurancePayout(mindId, args.Target);
    }

    /// <summary>
    /// Covers gibbing / decapitation / brain-MMI cases where the mob dies or the container is destroyed while the mind
    /// no longer registers on that entity when <see cref="MobState.Dead"/> fires on it.
    /// </summary>
    private void OnMindGotRemovedFromContainer(EntityUid mindId, MindComponent mind, MindGotRemovedEvent args)
    {
        var oldContainer = args.Container.Owner;
        if (!IsInsuranceDeathOriginatingContainer(oldContainer))
            return;

        TryActivateInsurancePayout(mindId, oldContainer);
    }

    private bool IsInsuranceDeathOriginatingContainer(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return true;

        if (TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Dead)
            return true;

        // Decapitated body may still not be MobState.Dead when the mind moves into the brain.
        if (HasComp<DebrainedComponent>(uid))
            return true;

        return false;
    }

    /// <summary>
    /// Life insurance data lives on the <see cref="MindComponent"/> entity (see <see cref="LifeInsuranceComponent"/>).
    /// </summary>
    private void TryActivateInsurancePayout(EntityUid mindId, EntityUid? stationSourceEntity)
    {
        var life = EnsureComp<LifeInsuranceComponent>(mindId);
        if (!life.IsInsured)
            return;

        var job = TryGetCurrentJob(mindId);
        if (job == null)
            return;

        life.IsInsured = false;
        life.PendingRespawnAt = _timing.CurTime + TimeSpan.FromMinutes(5);
        life.PendingRespawnJob = job;
        life.PendingRespawnStation = stationSourceEntity != null
            ? _station.GetOwningStation(stationSourceEntity.Value)
            : null;
        Dirty(mindId, life);
        PushInsuranceStatusToMind(mindId, true, life.PendingRespawnAt.Value);

        if (TryComp<MindComponent>(mindId, out var mindComp)
            && mindComp.Session?.AttachedEntity is { } current
            && TryComp<GhostComponent>(current, out var ghost))
        {
            SetInsuranceOnGhost(current, life.PendingRespawnAt.Value, ghost);
        }
    }

    private void OnGhostMindAdded(EntityUid uid, GhostComponent component, MindAddedMessage args)
    {
        var life = EnsureComp<LifeInsuranceComponent>(args.Mind.Owner);
        if (life.PendingRespawnAt is not { } respawnAt)
            return;

        SetInsuranceOnGhost(uid, respawnAt, component);
    }

    private void OnGhostStartup(EntityUid uid, GhostComponent component, ComponentStartup args)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return;

        var life = EnsureComp<LifeInsuranceComponent>(mindId);
        if (life.PendingRespawnAt is not { } respawnAt)
            return;

        SetInsuranceOnGhost(uid, respawnAt, component);
    }

    private void OnInsuranceRespawnRequest(GhostInsuranceRespawnRequest args, EntitySessionEventArgs eventArgs)
    {
        if (eventArgs.SenderSession.AttachedEntity is not { } ghostUid)
            return;

        if (!TryComp<GhostComponent>(ghostUid, out var ghostComp))
            return;

        if (!_mind.TryGetMind(ghostUid, out var mindId, out _))
            return;

        var life = EnsureComp<LifeInsuranceComponent>(mindId);
        if (life.PendingRespawnAt is not { } respawnAt
            || life.PendingRespawnJob is not { } job
            || _timing.CurTime < respawnAt)
            return;

        var station = life.PendingRespawnStation;
        if (station == null || TerminatingOrDeleted(station))
            station = _station.GetStations().FirstOrDefault();

        var profile = _ticker.GetPlayerProfile(eventArgs.SenderSession);
        var spawned = _stationSpawning.SpawnPlayerCharacterOnStation(station, job, profile);
        if (spawned is not { } spawnedUid)
            return;

        _mind.TransferTo(mindId, spawnedUid, ghostCheckOverride: true, createGhost: false);

        // Same mind as before ghost: job role usually already exists. Calling MindAddJobRole with the same job
        // would duplicate the role entity (SharedRoleSystem else-branch). Only add/update when missing or different.
        if (TryComp<MindComponent>(mindId, out var mindComp)
            && (!_roles.MindHasRole<JobRoleComponent>((mindId, mindComp), out var jr)
                || jr.Value.Comp1.JobPrototype != job))
        {
            _roles.MindAddJobRole(mindId, mindComp, silent: true, jobPrototype: job);
        }

        var stationUid = station ?? EntityUid.Invalid;
        _ticker.PlayersJoinedRoundNormally++;
        var spawnDone = new PlayerSpawnCompleteEvent(
            spawnedUid,
            eventArgs.SenderSession,
            job,
            lateJoin: true,
            silent: true,
            _ticker.PlayersJoinedRoundNormally,
            stationUid,
            profile);
        RaiseLocalEvent(spawnedUid, spawnDone, true);

        QueueDel(ghostUid);

        life.PendingRespawnAt = null;
        life.PendingRespawnJob = null;
        life.PendingRespawnStation = null;
        life.RespawnCount++;
        Dirty(mindId, life);
        PushInsuranceStatusToMind(mindId, false, TimeSpan.Zero);
    }

    private void UpdateUi(EntityUid uid, LifeInsuranceConsoleComponent component)
    {
        if (!_ui.IsUiOpen(uid, LifeInsuranceUiKey.Key))
            return;

        var state = BuildState(uid, component);
        _ui.SetUiState(uid, LifeInsuranceUiKey.Key, state);
    }

    private LifeInsuranceConsoleState BuildState(EntityUid uid, LifeInsuranceConsoleComponent component)
    {
        var storedProteins = _materialStorage.GetMaterialAmount(uid, component.ProteinMaterialId);
        var targets = new List<LifeInsuranceTargetEntry>();

        var consoleStation = _station.GetOwningStation(uid);
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } playerUid)
                continue;

            if (!_mind.TryGetMind(playerUid, out var mindId, out _))
                continue;

            var life = EnsureComp<LifeInsuranceComponent>(mindId);

            // Pending payout is tied to the station stored at death — not where the ghost wanders after.
            if (life.PendingRespawnAt != null && life.PendingRespawnStation is { } payoutStation)
            {
                if (payoutStation != consoleStation)
                    continue;
            }
            else if (_station.GetOwningStation(playerUid) != consoleStation)
            {
                continue;
            }

            var isGhostPending = TryComp<GhostComponent>(playerUid, out _) && life.PendingRespawnAt != null;
            var isAlive = TryComp<MobStateComponent>(playerUid, out var mobState)
                && mobState.CurrentState == MobState.Alive;

            if (!isAlive && !isGhostPending)
                continue;

            var roleName = Loc.GetString("generic-unknown");
            var job = TryGetCurrentJob(mindId);
            if (job != null && _prototype.TryIndex(job.Value, out JobPrototype? proto))
                roleName = proto.LocalizedName;

            var nextCredits = GetCurrentPrice(component.BaseRequiredCredits, life.RespawnCount);
            targets.Add(new LifeInsuranceTargetEntry(
                GetNetEntity(playerUid),
                Name(playerUid),
                roleName,
                life.IsInsured,
                life.PendingRespawnAt != null,
                nextCredits));
        }

        targets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return new LifeInsuranceConsoleState(
            storedProteins,
            component.RequiredProteins,
            targets);
    }

    private ProtoId<JobPrototype>? TryGetCurrentJob(EntityUid mindId)
    {
        if (!TryComp<MindComponent>(mindId, out var mind))
            return null;

        foreach (var role in mind.MindRoles)
        {
            if (TryComp<MindRoleComponent>(role, out var mindRole) && mindRole.JobPrototype is { } job)
                return job;
        }

        return null;
    }

    private static int GetCurrentPrice(int basePrice, int respawnCount)
    {
        var clamped = Math.Clamp(respawnCount, 0, 30);
        long multiplier = 1L << clamped;
        var total = (long)basePrice * multiplier;
        if (total >= int.MaxValue)
            return int.MaxValue;
        return (int)total;
    }

    private void SetInsuranceOnGhost(EntityUid ghostUid, TimeSpan respawnAt, GhostComponent? ghost = null)
    {
        if (!Resolve(ghostUid, ref ghost))
            return;

        _ghost.SetInsuranceRespawnData(ghostUid, true, respawnAt, ghost);

        if (!TryComp<MindContainerComponent>(ghostUid, out var mindContainer)
            || mindContainer.Mind is not { } mindId)
            return;

        PushInsuranceStatusToMind(mindId, true, respawnAt);
    }

    private void SyncGhostInsuranceButtons()
    {
        var query = EntityQueryEnumerator<GhostComponent, MindContainerComponent>();
        while (query.MoveNext(out var ghostUid, out var ghost, out var mindContainer))
        {
            if (mindContainer.Mind is not { } mindId
                || !TryComp<LifeInsuranceComponent>(mindId, out var life)
                || life.PendingRespawnAt is not { } respawnAt)
            {
                if (ghost.InsuranceRespawnAvailable)
                {
                    _ghost.SetInsuranceRespawnData(ghostUid, false, TimeSpan.Zero, ghost);
                    if (mindContainer.Mind is { } existingMind)
                        PushInsuranceStatusToMind(existingMind, false, TimeSpan.Zero);
                }
                continue;
            }

            _ghost.SetInsuranceRespawnData(ghostUid, true, respawnAt, ghost);
            PushInsuranceStatusToMind(mindId, true, respawnAt);
        }
    }

    private void PushInsuranceStatusToMind(EntityUid mindId, bool available, TimeSpan respawnAt)
    {
        if (!TryComp<MindComponent>(mindId, out var mind) || mind.Session == null)
            return;

        RaiseNetworkEvent(new GhostInsuranceRespawnStatusEvent(available, respawnAt), mind.Session.Channel);
    }
}
