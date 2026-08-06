using System.Threading.Tasks;

namespace PSuite.Core
{
    // Pure dispatch by engine type. Core/ModuleManager never branch on
    // engine — only this class does. Adding a new engine means adding
    // one case here, nowhere else.
    public static class ModuleLoader
    {
        public static Task<ModuleDetectResult> DetectAsync(InstalledModule module)
        {
            return module.Manifest.Engine switch
            {
                "Script" => ScriptEngine.DetectAsync(module.ResolvePath(module.Manifest.Entry.Detect!)),
                "Native" => NativeModuleHost.DetectAsync(module),
                _ => Task.FromResult(NotSupported<ModuleDetectResult>(module))
            };
        }

        public static Task<ModuleApplyResult> ApplyAsync(InstalledModule module, string capturePath)
        {
            return module.Manifest.Engine switch
            {
                "Script" => ScriptEngine.ApplyAsync(module.ResolvePath(module.Manifest.Entry.Apply!), capturePath),
                "Native" => NativeModuleHost.ApplyAsync(module, capturePath),
                _ => Task.FromResult(NotSupported<ModuleApplyResult>(module))
            };
        }

        public static Task<ModuleRollbackResult> RollbackAsync(InstalledModule module, string capturePath)
        {
            return module.Manifest.Engine switch
            {
                "Script" => ScriptEngine.RollbackAsync(module.ResolvePath(module.Manifest.Entry.Rollback!), capturePath),
                "Native" => NativeModuleHost.RollbackAsync(module, capturePath),
                _ => Task.FromResult(NotSupported<ModuleRollbackResult>(module))
            };
        }

        // Script Engine V5: "После Apply Script Engine автоматически
        // выполняет Validate". Optional — a module that doesn't declare
        // entry.verify (Script engine) or override Verify() (Native
        // engine) is treated as not supporting a separate validation step,
        // and callers should fall back to a plain Detect() themselves.
        public static Task<ModuleDetectResult>? VerifyAsync(InstalledModule module)
        {
            return module.Manifest.Engine switch
            {
                "Script" when !string.IsNullOrWhiteSpace(module.Manifest.Entry.Verify) =>
                    ScriptEngine.DetectAsync(module.ResolvePath(module.Manifest.Entry.Verify!)),
                "Native" => NativeModuleHost.VerifyAsync(module),
                _ => null
            };
        }

        private static T NotSupported<T>(InstalledModule module) where T : class, new()
        {
            // Hybrid (per-step engine mixing) is the only one left
            // unimplemented — fail clearly instead of pretending.
            var result = new T();
            typeof(T).GetProperty("Success")?.SetValue(result, false);
            typeof(T).GetProperty("Error")?.SetValue(result,
                $"Движок '{module.Manifest.Engine}' пока не реализован для модуля '{module.Manifest.Id}'.");
            return result;
        }
    }
}
