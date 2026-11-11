using WinCleaner.Core.Actions;
using WinCleaner.Core.Compatibility;
using WinCleaner.Core.Models;
using WinCleaner.Core.Operations;
using WinCleaner.Data;

namespace WinCleaner.Core.Services;

/// <summary>
/// Builds IChangeAction instances from JSON catalogs and resolves localized display text via a callback.
/// </summary>
public sealed class TweakActionFactory
{
    private readonly RegistryManager _registry;
    private readonly ServiceManager _services;
    private readonly PowerPlanManager _power;
    private readonly AppxManager _appx;
    private readonly TaskSchedulerManager _tasks;
    private readonly WingetInstallerService _winget;
    private readonly Func<string, string> _localize;
    private readonly OsInfo _os;

    public TweakActionFactory(
        RegistryManager registry,
        ServiceManager services,
        PowerPlanManager power,
        AppxManager appx,
        TaskSchedulerManager tasks,
        Func<string, string> localize,
        OsInfo os,
        WingetInstallerService? winget = null)
    {
        _registry = registry;
        _services = services;
        _power = power;
        _appx = appx;
        _tasks = tasks;
        _localize = localize;
        _os = os;
        _winget = winget ?? new WingetInstallerService();
    }

    public IChangeAction? CreateFromTweak(TweakCatalogEntry entry)
    {
        var name = _localize(entry.DisplayNameKey);
        var desc = _localize(entry.DescriptionKey);
        var risk = RiskParser.Parse(entry.Risk);
        var resolved = ResolveRegistryEntry(entry);

        return entry.Type.ToLowerInvariant() switch
        {
            "visualeffects" =>
                CreateVisualEffectsAction(entry, name, desc, risk),
            "registry" when resolved.Registries is { Count: > 0 } =>
                new MultiRegistryChangeAction(resolved, name, desc, _registry),
            "registry" when resolved.Registry is not null =>
                new RegistryChangeAction(resolved, name, desc, _registry),
            "powerplan" =>
                new PowerPlanChangeAction(entry.Id, name, desc, risk, _power),
            "command" when entry.Command is not null =>
                new CommandChangeAction(entry.Id, name, desc, entry.Category, risk, entry.Command, _localize, entry.RequiresReboot, entry.RequiresAntiCheatConfirm, ActionConstraints.From(entry.Skus, entry.Archs, entry.RequiresFeature)),
            "service" when entry.Service is not null =>
                CreateServiceAction(entry.Id, name, desc, entry.Category, risk, entry.Service.ServiceName, entry.Service.DesiredStart),
            "task" when entry.Task is not null =>
                new TaskDisableAction(entry.Id, name, desc, entry.Task.TaskPath, _tasks),
            _ => null
        };
    }

    private IChangeAction CreateVisualEffectsAction(
        TweakCatalogEntry entry,
        string name,
        string desc,
        RiskLevel risk)
    {
        var mode = entry.Id.Contains("animation", StringComparison.OrdinalIgnoreCase)
            ? VisualEffectsChangeAction.Mode.DisableAnimations
            : VisualEffectsChangeAction.Mode.BestPerformance;

        return new VisualEffectsChangeAction(
            entry.Id, name, desc, entry.Category, risk, mode, _registry);
    }

    /// <summary>Picks the registry spec matching the current OS build from registryVariants.</summary>
    public TweakCatalogEntry ResolveRegistryEntry(TweakCatalogEntry entry)
    {
        if (entry.RegistryVariants is not { Count: > 0 })
            return entry;

        var build = _os.BuildNumber;
        RegistryBuildVariant? match = null;
        foreach (var variant in entry.RegistryVariants)
        {
            if (variant.MinBuild.HasValue && build < variant.MinBuild.Value)
                continue;
            if (variant.MaxBuild.HasValue && build > variant.MaxBuild.Value)
                continue;
            match = variant;
            break;
        }

        if (match is null)
            return entry;

        return new TweakCatalogEntry
        {
            Id = entry.Id,
            DisplayNameKey = entry.DisplayNameKey,
            DescriptionKey = entry.DescriptionKey,
            Category = entry.Category,
            Risk = entry.Risk,
            Type = entry.Type,
            MinBuild = entry.MinBuild,
            Win11Only = entry.Win11Only,
            RequiresAntiCheatConfirm = entry.RequiresAntiCheatConfirm,
            RequiresReboot = entry.RequiresReboot,
            Registry = match.Registries is { Count: > 0 } ? null : match.Registry ?? entry.Registry,
            Registries = match.Registries is { Count: > 0 } ? match.Registries : entry.Registries,
            Service = entry.Service,
            Task = entry.Task,
            PowerPlan = entry.PowerPlan,
            Command = entry.Command
        };
    }

    public ServiceChangeAction CreateServiceAction(
        string id, string name, string desc, string category, RiskLevel risk,
        string serviceName, string desiredStart, IEnumerable<string>? aliases = null)
    {
        var resolved = _services.ResolveExistingName(serviceName, aliases);
        var target = desiredStart.ToLowerInvariant() switch
        {
            "automatic" or "auto" => ServiceStartModeTarget.Automatic,
            "manual" or "demand" => ServiceStartModeTarget.Manual,
            _ => ServiceStartModeTarget.Disabled
        };
        return new ServiceChangeAction(id, name, desc, category, risk, resolved, target, _services);
    }

    public AppxRemoveAction CreateAppxRemove(AppCatalogEntry entry) =>
        new(entry.Id, _localize(entry.DisplayNameKey), _localize(entry.DescriptionKey),
            RiskParser.Parse(entry.Risk), entry.GetPackageNamePatterns(), _appx, entry.SystemCritical);

    public AppxInstallAction? CreateAppxInstall(AppCatalogEntry entry)
    {
        var installId = entry.GetInstallId();
        if (string.IsNullOrWhiteSpace(installId))
            return null;
        return new AppxInstallAction(
            entry.Id + "-install",
            _localize(entry.DisplayNameKey),
            _localize(entry.DescriptionKey),
            RiskParser.Parse(entry.Risk),
            installId,
            _winget);
    }

    public OneDriveUninstallAction CreateOneDriveUninstall() => new(_appx, _localize);

    public EdgeUninstallAction CreateEdgeUninstall() => new(_appx, _localize);
}

public sealed class PresetEngine
{
    private readonly TweakActionFactory _factory;
    private readonly ActionExecutor _executor;
    private readonly OsInfo _os;

    public PresetEngine(TweakActionFactory factory, ActionExecutor executor, OsInfo os)
    {
        _factory = factory;
        _executor = executor;
        _os = os;
    }

    public IReadOnlyList<IChangeAction> BuildPreview(PresetCatalogEntry preset)
    {
        var profile = new CustomProfileDocument
        {
            Id = preset.Id,
            Name = preset.DisplayNameKey,
            Description = preset.DescriptionKey,
            TweakIds = preset.TweakIds?.ToList() ?? new List<string>(),
            ServiceIds = preset.ServiceIds?.ToList() ?? new List<string>()
        };
        return BuildPreview(profile);
    }

    /// <summary>
    /// Builds actions from a full custom profile (v2) or legacy serviceIds list.
    /// </summary>
    public IReadOnlyList<IChangeAction> BuildPreview(CustomProfileDocument profile)
    {
        var actions = new List<IChangeAction>();
        var tweaks = CatalogLoader.LoadTweaks().ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var services = CatalogLoader.LoadServices().ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var id in profile.TweakIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!tweaks.TryGetValue(id, out var tweak)) continue;
            if (!OsCompatibility.IsCompatible(tweak.MinBuild, tweak.Win11Only, _os)) continue;
            if (!CatalogAccess.Allows(tweak, _os.Sku, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)) continue;
            var action = _factory.CreateFromTweak(tweak);
            if (action is not null) actions.Add(action);
        }

        if (profile.Services is { Count: > 0 })
        {
            foreach (var entry in profile.Services)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) continue;
                if (!services.TryGetValue(entry.Id, out var svc)) continue;
                if (!OsCompatibility.IsCompatible(svc.MinBuild, svc.Win11Only, _os)) continue;
                var risk = RiskParser.Parse(svc.Risk);
                if (risk == RiskLevel.Blocked) continue;
                var start = string.IsNullOrWhiteSpace(entry.StartType) ? "Disabled" : entry.StartType;
                actions.Add(_factory.CreateServiceAction(
                    svc.Id,
                    svc.ServiceName,
                    svc.DescriptionKey,
                    svc.Category,
                    risk,
                    svc.ServiceName,
                    start,
                    svc.ServiceAliases));
            }
        }
        else if (profile.ServiceIds is { Count: > 0 })
        {
            // Legacy: apply catalog recommended start type
            foreach (var id in profile.ServiceIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!services.TryGetValue(id, out var svc)) continue;
                if (!OsCompatibility.IsCompatible(svc.MinBuild, svc.Win11Only, _os)) continue;
                var risk = RiskParser.Parse(svc.Risk);
                if (risk == RiskLevel.Blocked) continue;
                actions.Add(_factory.CreateServiceAction(
                    svc.Id,
                    svc.ServiceName,
                    svc.DescriptionKey,
                    svc.Category,
                    risk,
                    svc.ServiceName,
                    svc.RecommendedStart,
                    svc.ServiceAliases));
            }
        }

        return actions;
    }

    public Task<IReadOnlyList<ChangeResult>> ApplyAsync(
        PresetCatalogEntry preset,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        _executor.ExecuteManyAsync(BuildPreview(preset), progress, ct);
}

/// <summary>
/// Application composition root for Core services (simple DI without a container).
/// </summary>
public sealed class AppServices
{
    public AppSettings Settings { get; }
    public OsInfo Os { get; }
    public SystemHardwareInfo Hardware { get; }
    public ServiceManager Services { get; } = new();
    public RegistryManager Registry { get; } = new();
    public AppxManager Appx { get; } = new();
    public RestorePointManager RestorePoints { get; } = new();
    public TaskSchedulerManager Tasks { get; } = new();
    public PowerPlanManager PowerPlans { get; } = new();
    public TempCleanupService TempCleanup { get; } = new();
    public StartupManager Startup { get; } = new();
    public SystemHealthService SystemHealth { get; } = new();
    public DnsChangerService Dns { get; } = new();
    public WingetInstallerService Winget { get; } = new();
    public RegistryTransfer RegistryTransfer { get; } = new(ProcessRunner.Shared);
    public ChangeLogService ChangeLog { get; }
    public SnapshotStore Snapshots { get; }
    public ActionExecutor Executor { get; }
    public IOperationRunner Operations { get; }
    public SessionLineLog SessionLog { get; }
    public TweakActionFactory ActionFactory { get; private set; } = null!;
    public PresetEngine Presets { get; private set; } = null!;
    public AppliedStateCache AppliedState { get; } = new();

    public AppServices(Func<string, string> localize)
    {
        Settings = AppSettings.Load();
        Os = OsCompatibility.Detect();
        Hardware = SystemInfoService.Collect(Os);
        ChangeLog = new ChangeLogService(Settings);
        Snapshots = new SnapshotStore(Settings);
        Executor = new ActionExecutor(ChangeLog, RestorePoints, Settings, Snapshots);
        SessionLog = new SessionLineLog(Settings.GetLogFolder());
        Operations = new OperationRunner(SessionLog, new MachineSystemInfo(), () => Executor.Confirmation);
        SetLocalizer(localize);
    }

    public Task<OperationResult> RunActionsAsync(
        string name,
        IReadOnlyList<IChangeAction> actions,
        string? snapshotName,
        CancellationToken cancellationToken = default,
        bool antiCheatConfirmed = false) =>
        Operations.EnqueueAsync(new OperationRequest(name, true, async ctx =>
        {
            IProgress<string> progress = new InlineProgress<string>(text => ctx.Progress.Report(new OperationStep(text, null)));
            var results = await Executor.ExecuteManyAsync(actions, progress, ctx.Token, snapshotName, antiCheatConfirmed).ConfigureAwait(false);
            var failed = results.Where(r => !r.Success).Select(r => r.Message ?? r.ErrorCode).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (failed.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, failed));
        }), cancellationToken);

    public Task<OperationResult> RunActionAsync(
        string name,
        IChangeAction action,
        bool isRevert = false,
        CancellationToken cancellationToken = default) =>
        Operations.EnqueueAsync(new OperationRequest(name, true, async ctx =>
        {
            ctx.Progress.Report(new OperationStep(name, null));
            var result = await Executor.ExecuteAsync(action, isRevert, ctx.Token).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException(result.Message ?? result.ErrorCode ?? name);
        }), cancellationToken);

    public Task<OperationResult> RunWorkAsync(
        string name,
        bool writesSystem,
        Func<OperationContext, Task> body,
        CancellationToken cancellationToken = default) =>
        Operations.EnqueueAsync(new OperationRequest(name, writesSystem, body), cancellationToken);

    public void SetLocalizer(Func<string, string> localize)
    {
        ActionFactory = new TweakActionFactory(Registry, Services, PowerPlans, Appx, Tasks, localize, Os, Winget);
        Presets = new PresetEngine(ActionFactory, Executor, Os);
    }

    /// <summary>Ensure a single session restore point exists (used at startup; not before each tweak).</summary>
    public Task EnsureSessionRestoreAsync(CancellationToken ct = default) =>
        RestorePoints.EnsureSessionCheckpointAsync(Settings.AutoRestorePoint, warnIfMissing: false, ct);
}
