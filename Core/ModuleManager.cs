using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PSuite.Core
{
    public class ModuleValidationError
    {
        public string ModuleFolder { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class ModuleManager
    {
        private readonly string _modulesRoot;
        private readonly Dictionary<string, InstalledModule> _modules = new();
        private readonly List<ModuleValidationError> _lastValidationErrors = new();

        private static readonly HashSet<string> ValidRisks = new(StringComparer.OrdinalIgnoreCase)
        {
            "Safe", "Advanced", "Experimental"
        };

        private static readonly HashSet<string> ValidEngines = new(StringComparer.OrdinalIgnoreCase)
        {
            "Script", "Native", "Hybrid"
        };

        public ModuleManager(string modulesRoot)
        {
            _modulesRoot = modulesRoot;
            Directory.CreateDirectory(_modulesRoot);
        }

        public IReadOnlyCollection<InstalledModule> Modules => _modules.Values;
        public IReadOnlyList<ModuleValidationError> LastValidationErrors => _lastValidationErrors;

        public void Refresh()
        {
            _modules.Clear();
            _lastValidationErrors.Clear();

            foreach (var folder in Directory.EnumerateDirectories(_modulesRoot))
            {
                TryLoadModule(folder);
            }
        }

        // Same as Refresh(), off the UI thread. With one demo module the
        // synchronous version is instant either way, but this is the
        // platform's actual scaling path: hundreds of manifest.json reads
        // during startup/refresh would otherwise freeze the window.
        public Task RefreshAsync() => Task.Run(Refresh);

        // "Из папки" entry point. Smart about what was actually selected:
        //  - manifest.json right there            → import that one module
        //  - no manifest, but subfolders that have one → import ALL of
        //    them in one go (this is the common case: someone points it at
        //    the whole "Modules" folder with many module subfolders) and
        //    report a summary instead of refusing and making them pick
        //    one folder at a time.
        public string? TryImportFolder(string sourceFolder)
        {
            var manifestPath = Path.Combine(sourceFolder, "manifest.json");
            if (File.Exists(manifestPath))
                return TryImportSingleFolder(sourceFolder);

            var subFolders = Directory.Exists(sourceFolder) ? Directory.GetDirectories(sourceFolder) : Array.Empty<string>();
            var withManifest = subFolders.Where(f => File.Exists(Path.Combine(f, "manifest.json"))).ToArray();

            if (withManifest.Length == 0)
                return "Папка не содержит manifest.json ни в себе, ни в подпапках.";

            if (withManifest.Length == 1)
                return TryImportSingleFolder(withManifest[0]);

            var successCount = 0;
            var failures = new List<string>();
            foreach (var folder in withManifest)
            {
                var error = TryImportSingleFolder(folder);
                if (error == null) successCount++;
                else failures.Add($"{Path.GetFileName(folder)}: {error}");
            }

            if (failures.Count == 0)
                return null; // all imported silently — caller shows its own success message

            var summary = $"Импортировано {successCount} из {withManifest.Length}. Ошибки: " +
                           string.Join(" | ", failures.Take(5)) +
                           (failures.Count > 5 ? $" | ещё {failures.Count - 5}..." : "");
            return summary;
        }

        private string? TryImportSingleFolder(string sourceFolder)
        {
            var manifestPath = Path.Combine(sourceFolder, "manifest.json");
            if (!File.Exists(manifestPath))
                return "Папка не содержит manifest.json.";

            var (manifest, error) = ParseManifest(manifestPath);
            if (manifest == null)
                return error;

            if (_modules.ContainsKey(manifest.Id))
                return $"Модуль с id '{manifest.Id}' уже установлен.";

            var destination = Path.Combine(_modulesRoot, manifest.Id);
            if (Directory.Exists(destination))
                return $"Папка '{manifest.Id}' уже существует в Modules/.";

            CopyDirectory(sourceFolder, destination);

            var (reloaded, reloadError) = ParseManifest(Path.Combine(destination, "manifest.json"));
            if (reloaded == null)
            {
                Directory.Delete(destination, recursive: true);
                return reloadError ?? "Не удалось загрузить модуль после копирования.";
            }

            _modules[reloaded.Id] = new InstalledModule(reloaded, destination);
            return null;
        }

        // Extracts a .zip to a temp folder and imports it as if it were a
        // folder — including the batch case (a zip of many module
        // subfolders imports all of them, same as TryImportFolder).
        public string? TryImportZip(string zipPath)
        {
            if (!File.Exists(zipPath))
                return "Файл .zip не найден.";

            var tempDir = Path.Combine(Path.GetTempPath(), "psuite-import-" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(zipPath, tempDir);
                return TryImportFolder(tempDir);
            }
            catch (InvalidDataException)
            {
                return "Файл повреждён или это не .zip архив.";
            }
            catch (Exception ex)
            {
                return $"Не удалось распаковать архив: {ex.Message}";
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }

        private void TryLoadModule(string folder)
        {
            var manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                _lastValidationErrors.Add(new ModuleValidationError
                {
                    ModuleFolder = folder,
                    Reason = "Отсутствует manifest.json."
                });
                return;
            }

            var (manifest, error) = ParseManifest(manifestPath);
            if (manifest == null)
            {
                _lastValidationErrors.Add(new ModuleValidationError
                {
                    ModuleFolder = folder,
                    Reason = error ?? "Неизвестная ошибка валидации."
                });
                return;
            }

            if (_modules.ContainsKey(manifest.Id))
            {
                _lastValidationErrors.Add(new ModuleValidationError
                {
                    ModuleFolder = folder,
                    Reason = $"Дублирующийся id '{manifest.Id}' — уже загружен из другой папки."
                });
                return;
            }

            _modules[manifest.Id] = new InstalledModule(manifest, folder);
        }

        private (ModuleManifest? manifest, string? error) ParseManifest(string manifestPath)
        {
            ModuleManifest? manifest;
            try
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize<ModuleManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                return (null, $"manifest.json содержит невалидный JSON: {ex.Message}");
            }

            if (manifest == null)
                return (null, "manifest.json пуст или не удалось десериализовать.");

            if (string.IsNullOrWhiteSpace(manifest.Id))
                return (null, "Отсутствует обязательное поле 'id'.");

            if (string.IsNullOrWhiteSpace(manifest.Name))
                return (null, "Отсутствует обязательное поле 'name'.");

            if (!ValidRisks.Contains(manifest.Risk))
                return (null, $"Поле 'risk' имеет недопустимое значение '{manifest.Risk}'. " +
                               "Допустимо: Safe, Advanced, Experimental. " +
                               "'Blocked' назначается только PSuite, модуль не может объявить его сам.");

            if (!ValidEngines.Contains(manifest.Engine))
                return (null, $"Поле 'engine' имеет недопустимое значение '{manifest.Engine}'. " +
                               "Допустимо: Script, Native, Hybrid.");

            if (string.Equals(manifest.Engine, "Script", StringComparison.OrdinalIgnoreCase))
            {
                var folder = Path.GetDirectoryName(manifestPath)!;

                if (string.IsNullOrWhiteSpace(manifest.Entry.Detect))
                    return (null, "Поле 'entry.detect' отсутствует или пусто в manifest.json.");
                var detectPath = Path.Combine(folder, manifest.Entry.Detect);
                if (!File.Exists(detectPath))
                    return (null, $"Файл detect-скрипта не найден по пути: {detectPath} (из entry.detect='{manifest.Entry.Detect}').");

                if (string.IsNullOrWhiteSpace(manifest.Entry.Apply))
                    return (null, "Поле 'entry.apply' отсутствует или пусто в manifest.json.");
                var applyPath = Path.Combine(folder, manifest.Entry.Apply);
                if (!File.Exists(applyPath))
                    return (null, $"Файл apply-скрипта не найден по пути: {applyPath} (из entry.apply='{manifest.Entry.Apply}').");

                if (manifest.SupportsRollback)
                {
                    if (string.IsNullOrWhiteSpace(manifest.Entry.Rollback))
                        return (null, "'supportsRollback' = true, но поле 'entry.rollback' отсутствует или пусто.");
                    var rollbackPath = Path.Combine(folder, manifest.Entry.Rollback);
                    if (!File.Exists(rollbackPath))
                        return (null, $"'supportsRollback' = true, но файл rollback-скрипта не найден по пути: {rollbackPath} (из entry.rollback='{manifest.Entry.Rollback}').");
                }
            }

            if (string.Equals(manifest.Engine, "Native", StringComparison.OrdinalIgnoreCase))
            {
                var folder = Path.GetDirectoryName(manifestPath)!;

                if (manifest.Native == null ||
                    string.IsNullOrWhiteSpace(manifest.Native.Assembly) ||
                    string.IsNullOrWhiteSpace(manifest.Native.Type))
                    return (null, "Для engine=\"Native\" обязательно поле 'native' с заполненными 'assembly' и 'type'.");

                if (!File.Exists(Path.Combine(folder, manifest.Native.Assembly)))
                    return (null, $"Не найдена сборка '{manifest.Native.Assembly}', указанная в поле 'native.assembly'.");
            }

            return (manifest, null);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}