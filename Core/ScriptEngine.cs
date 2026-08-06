using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PSuite.Core.Runners;

namespace PSuite.Core
{
    // Script Engine V5. Runs a single detect/apply/rollback script per
    // MODULE_SPEC. No interactive prompts, bounded by timeout, last
    // stdout line is parsed as JSON.
    //
    // Per the V5 principle "ядро не запускає BAT напряму, тільки через
    // Runner": this class itself never builds a ProcessStartInfo or
    // touches cmd.exe/powershell.exe/reg.exe directly. It asks
    // RunnerFactory for whichever IScriptRunner handles the file's
    // extension, runs it, and adapts the runner-agnostic
    // RunnerExecutionResult into the ModuleDetectResult/ModuleApplyResult/
    // ModuleRollbackResult shapes the rest of Core already expects.
    // Adding BAT/CMD/REG/PowerShell today, or Python/Lua/Rust/Native
    // tomorrow, never touches this file — only RunnerFactory's list.
    public static class ScriptEngine
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public static Task<ModuleDetectResult> DetectAsync(string scriptPath, TimeSpan? timeout = null)
            => RunAsync<ModuleDetectResult>(scriptPath, capturePath: null, timeout);

        public static Task<ModuleApplyResult> ApplyAsync(string scriptPath, string capturePath, TimeSpan? timeout = null)
            => RunAsync<ModuleApplyResult>(scriptPath, capturePath, timeout);

        public static Task<ModuleRollbackResult> RollbackAsync(string scriptPath, string capturePath, TimeSpan? timeout = null)
            => RunAsync<ModuleRollbackResult>(scriptPath, capturePath, timeout);

        private static async Task<T> RunAsync<T>(string scriptPath, string? capturePath, TimeSpan? timeout)
            where T : class, new()
        {
            var effectiveTimeout = timeout ?? DefaultTimeout;

            if (!File.Exists(scriptPath))
                return MakeFailure<T>($"Файл скрипта не найден: {scriptPath}");

            var runner = RunnerFactory.Resolve(scriptPath);
            if (runner == null)
            {
                var extension = Path.GetExtension(scriptPath);
                return MakeFailure<T>(
                    $"Неподдерживаемый тип скрипта '{extension}'. Используйте .bat, .cmd, .reg или (в крайнем случае) .ps1.");
            }

            RunnerExecutionResult execResult;
            try
            {
                execResult = await runner.RunAsync(scriptPath, capturePath, effectiveTimeout);
            }
            catch (Exception ex)
            {
                return MakeFailure<T>($"Runner '{runner.Name}' выбросил исключение: {ex.Message}");
            }

            if (execResult.TimedOut)
                return MakeFailure<T>(execResult.ErrorMessage ?? "Скрипт превысил время ожидания.");

            if (!execResult.Success)
                return MakeFailure<T>(execResult.ErrorMessage ?? $"Скрипт завершился с кодом {execResult.ExitCode}.");

            var lastLine = GetLastNonEmptyLine(execResult.ConsoleOutput);
            if (string.IsNullOrWhiteSpace(lastLine))
            {
                // A script that exits 0 but prints nothing at all almost
                // always means a command inside it failed silently and
                // the rest of the batch file (including the final echo of
                // JSON) never ran — most commonly because it needed admin
                // rights that PSuite wasn't granted, or hit a command that
                // doesn't exist/isn't on PATH in the current session.
                return MakeFailure<T>(
                    $"Скрипт завершился без ошибки (код {execResult.ExitCode}), но не вывел JSON в конце. " +
                    "Обычно это значит, что одна из команд внутри скрипта не выполнилась (например, " +
                    "не хватило прав администратора) и до финального echo с JSON выполнение не дошло — " +
                    "проверь requiresAdmin в manifest.json и запусти PSuite от имени администратора.");
            }

            try
            {
                var result = JsonSerializer.Deserialize<T>(lastLine, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return result ?? MakeFailure<T>("Не удалось разобрать вывод скрипта.");
            }
            catch (JsonException)
            {
                return MakeFailure<T>("Последняя строка вывода скрипта — невалидный JSON.");
            }
        }

        private static string? GetLastNonEmptyLine(string text)
        {
            var lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var trimmed = lines[i].Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    return trimmed;
            }
            return null;
        }

        private static T MakeFailure<T>(string error) where T : class, new()
        {
            var result = new T();
            var errorProp = typeof(T).GetProperty("Error");
            var successProp = typeof(T).GetProperty("Success");
            errorProp?.SetValue(result, error);
            successProp?.SetValue(result, false);
            return result;
        }
    }
}
