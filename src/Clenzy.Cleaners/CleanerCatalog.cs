using Clenzy.Cleaners.Browsers;
using Clenzy.Cleaners.Privacy;
using Clenzy.Cleaners.Registry;
using Clenzy.Cleaners.SystemJunk;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Services;

namespace Clenzy.Cleaners;

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
        IBrowserCloser browserCloser,
        IAppLogger log)
    {
        var modules = new List<ICleanerModule>();
        modules.AddRange(SystemModuleFactory.CreateFileModules(paths, deletion, log));
        modules.Add(new RecycleBinModule(recycleBin, log));
        modules.Add(new WindowsUpdateCleanupModule(paths, fileProbe, processRunner, log));
        modules.Add(new DeliveryOptimizationModule(paths, fileProbe, processRunner, log));
        modules.Add(new ChromiumBrowserModule(BrowserCatalog.Chrome(paths), paths, browserDetector, browserCloser, fileProbe, deletion, log));
        modules.Add(new ChromiumBrowserModule(BrowserCatalog.Edge(paths), paths, browserDetector, browserCloser, fileProbe, deletion, log));
        modules.Add(new RegistryModule(registryScanner, log));
        modules.Add(new PrivacyModule(registry, paths, deletion, log));
        return modules;
    }
}
