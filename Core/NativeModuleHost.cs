using System;
using System.IO;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace PSuite.Core
{
    // Loads and runs Native-engine modules: a module-supplied .dll that
    // implements IPSuiteModule directly, for tweaks that genuinely can't
    // be expressed as a BAT/PS1 script (driver-level work, Win32 APIs
    // with no CLI surface, etc). This is the exception, not the default —
    // ScriptEngine (BAT/CMD) remains the primary format per MODULE_SPEC.
    //
    // Each call gets its own collectible AssemblyLoadContext so one
    // module's dependencies can never collide with another's. Core never
    // talks to the module type directly — only through IPSuiteModule.
    public static class NativeModuleHost
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public static Task<ModuleDetectResult> DetectAsync(InstalledModule module, TimeSpan? timeout = null)
            => RunAsync(module, m => m.Detect(), timeout);

        // V5's post-Apply Validate step for native modules — IPSuiteModule
        // already declares Verify() (nullable: a module can decline to
        // support it, falling back to a plain Detect()-based check).
        public static Task<ModuleDetectResult> VerifyAsync(InstalledModule module, TimeSpan? timeout = null)
            => RunAsync(module, m => m.Verify() ?? m.Detect(), timeout);

        public static Task<ModuleApplyResult> ApplyAsync(InstalledModule module, string capturePath, TimeSpan? timeout = null)
            => RunAsync(module, m => m.Apply(capturePath), timeout);

        public static Task<ModuleRollbackResult> RollbackAsync(InstalledModule module, string capturePath, TimeSpan? timeout = null)
            => RunAsync(module, m => m.Rollback(capturePath), timeout);

        private static async Task<T> RunAsync<T>(InstalledModule module, Func<IPSuiteModule, T> action, TimeSpan? timeout)
            where T : class, new()
        {
            var native = module.Manifest.Native;
            if (native == null || string.IsNullOrWhiteSpace(native.Assembly) || string.IsNullOrWhiteSpace(native.Type))
                return Fail<T>($"У модуля '{module.Manifest.Id}' engine=\"Native\", но поле 'native' (assembly/type) не заполнено в manifest.json.");

            var assemblyPath = module.ResolvePath(native.Assembly);
            if (!File.Exists(assemblyPath))
                return Fail<T>($"Не найдена сборка '{native.Assembly}' модуля '{module.Manifest.Id}'.");

            var effectiveTimeout = timeout ?? DefaultTimeout;
            var work = Task.Run(() => RunInIsolatedContext(module, assemblyPath, native, action));

            var completed = await Task.WhenAny(work, Task.Delay(effectiveTimeout));
            if (completed != work)
            {
                // .NET has no safe way to force-abort an arbitrary running
                // thread, so we can't kill the module's code here — we can
                // only stop waiting for it and tell the person honestly
                // that it didn't return in time. The orphaned call may
                // still be running in the background until it finishes
                // or the app process exits.
                return Fail<T>($"Native-модуль '{module.Manifest.Id}' не ответил за {effectiveTimeout.TotalSeconds} сек (таймаут).");
            }

            return await work;
        }

        private static T RunInIsolatedContext<T>(InstalledModule module, string assemblyPath, ModuleNativeInfo native, Func<IPSuiteModule, T> action)
            where T : class, new()
        {
            var context = new AssemblyLoadContext($"psuite-module-{module.Manifest.Id}", isCollectible: true);

            // A native module may ship its own dependency DLLs alongside
            // the main assembly. Resolve those from the module's own
            // folder first, before falling back to the default context —
            // this is what makes non-trivial (multi-file) native modules
            // actually work instead of throwing FileNotFoundException on
            // the first external reference.
            context.Resolving += (ctx, assemblyName) =>
            {
                if (assemblyName.Name == null) return null;
                var candidate = Path.Combine(module.FolderPath, assemblyName.Name + ".dll");
                return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
            };

            try
            {
                if (string.IsNullOrWhiteSpace(native.Type))
                    return Fail<T>($"У модуля '{module.Manifest.Id}' не заполнено поле 'native.type'.");

                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                var type = assembly.GetType(native.Type, throwOnError: false);
                if (type == null)
                    return Fail<T>($"Тип '{native.Type}' не найден в сборке '{native.Assembly}'.");

                if (Activator.CreateInstance(type) is not IPSuiteModule instance)
                    return Fail<T>($"Тип '{native.Type}' не реализует интерфейс IPSuiteModule.");

                return action(instance);
            }
            catch (Exception ex)
            {
                return Fail<T>($"Native-модуль '{module.Manifest.Id}' выбросил исключение: {ex.Message}");
            }
            finally
            {
                context.Unload();
            }
        }

        private static T Fail<T>(string error) where T : class, new()
        {
            var result = new T();
            typeof(T).GetProperty("Success")?.SetValue(result, false);
            typeof(T).GetProperty("Error")?.SetValue(result, error);
            return result;
        }
    }
}
