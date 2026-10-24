using CleanSlate.Cleaners.Browsers;
using CleanSlate.Cleaners.Privacy;
using CleanSlate.Cleaners.Registry;
using CleanSlate.Cleaners.SystemJunk;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;

namespace CleanSlate.Cleaners;

/// <summary>Creates the full, fixed set of cleaning modules. There is no dynamic or externally supplied module loading.</summary>
public static class CleanerCatalog
{
    public static IReadOnlyList<ICleanerModule> CreateModules(
        SystemPaths paths,
        ISafeDeletionService deletion,
        IRegistryAccess registry,
        RegistryScanner registryScanner,
        IFileProbe fileProbe,
        IProcessRunner processRunner,
        IRecycleBinService recycleBin,
        IBrowserRunningDetector browserDetector,
        IAppLogger log)
    {
        var modules = new List<ICleanerModule>();
        modules.AddRange(SystemModuleFactory.CreateFileModules(paths, deletion, log));
        modules.Add(new RecycleBinModule(recycleBin, log));
        modules.Add(new WindowsUpdateCleanupModule(paths, fileProbe, processRunner, log));
        modules.Add(new DeliveryOptimizationModule(paths, fileProbe, processRunner, log));
        modules.Add(new ChromiumBrowserModule(BrowserCatalog.Chrome(paths), paths, browserDetector, fileProbe, deletion, log));
        modules.Add(new ChromiumBrowserModule(BrowserCatalog.Edge(paths), paths, browserDetector, fileProbe, deletion, log));
        modules.Add(new RegistryModule(registryScanner, log));
        modules.Add(new PrivacyModule(registry, paths, deletion, log));
        return modules;
    }
}
