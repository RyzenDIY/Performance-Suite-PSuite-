using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PSuite.Core
{
    public class NetworkSpeedTestResult
    {
        public double PingMs { get; set; }
        public double JitterMs { get; set; }
        public double DownloadMbps { get; set; }
        public double UploadMbps { get; set; }
        public string ServerName { get; set; } = string.Empty;
    }

    // Wraps the bundled LibreSpeed CLI (Assets/librespeed-cli.exe) as an
    // external process — not a hand-rolled network implementation. This
    // is a genuine, actively-maintained, LGPL-3.0-licensed open-source
    // tool (https://github.com/librespeed/speedtest-cli); PSuite invokes
    // its already-compiled binary rather than reimplementing speed
    // testing, and ships its LICENSE file alongside it as required by
    // LGPL. Unlike Ookla's CLI, LibreSpeed's licence has no restriction
    // on bundling the binary inside another distributed application.
    //
    // Every failure mode reports a clear reason instead of throwing past
    // the caller — missing exe, malformed JSON, non-zero exit code, or
    // the process itself failing to start are all handled explicitly.
    public static class NetworkSpeedTest
    {
        private const string ExeRelativePath = "Assets\\librespeed-cli.exe";

        public static bool IsAvailable()
        {
            try
            {
                var exePath = Path.Combine(AppContext.BaseDirectory, ExeRelativePath);
                return File.Exists(exePath);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<(bool Success, string? Error, NetworkSpeedTestResult? Result)> RunAsync()
        {
            string exePath;
            try
            {
                exePath = Path.Combine(AppContext.BaseDirectory, ExeRelativePath);
                if (!File.Exists(exePath))
                {
                    return (false,
                        "librespeed-cli.exe не найден в папке Assets. Скачайте его с https://github.com/librespeed/speedtest-cli/releases и положите рядом с PSuite.exe.",
                        null);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Не удалось определить путь к librespeed-cli.exe: {ex.Message}", null);
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--json --no-icmp",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return (false, "Не удалось запустить librespeed-cli.exe.", null);

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    return (false,
                        string.IsNullOrWhiteSpace(stderr)
                            ? $"librespeed-cli завершился с кодом {process.ExitCode}."
                            : stderr.Trim(),
                        null);
                }

                if (string.IsNullOrWhiteSpace(stdout))
                    return (false, "librespeed-cli не вернул данных.", null);

                return ParseResult(stdout);
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка запуска librespeed-cli: {ex.Message}", null);
            }
        }

        // librespeed-cli --json outputs a JSON array with one object per
        // tested server (usually just one, since we don't pass
        // --multiple). Values are already in Mbps by default (not bytes,
        // since we don't pass --bytes).
        private static (bool, string?, NetworkSpeedTestResult?) ParseResult(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement entry;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0)
                        return (false, "librespeed-cli не нашёл ни одного сервера для теста.", null);
                    entry = root[0];
                }
                else
                {
                    entry = root;
                }

                double ReadDouble(string propertyName)
                {
                    if (entry.TryGetProperty(propertyName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d)) return d;
                        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var ds)) return ds;
                    }
                    return 0;
                }

                string serverName = "Неизвестный сервер";
                if (entry.TryGetProperty("server", out var serverProp) &&
                    serverProp.ValueKind == JsonValueKind.Object &&
                    serverProp.TryGetProperty("name", out var nameProp) &&
                    nameProp.ValueKind == JsonValueKind.String)
                {
                    serverName = nameProp.GetString() ?? serverName;
                }

                var result = new NetworkSpeedTestResult
                {
                    PingMs = ReadDouble("ping"),
                    JitterMs = ReadDouble("jitter"),
                    DownloadMbps = ReadDouble("download"),
                    UploadMbps = ReadDouble("upload"),
                    ServerName = serverName
                };

                return (true, null, result);
            }
            catch (JsonException ex)
            {
                return (false, $"Не удалось разобрать ответ librespeed-cli: {ex.Message}", null);
            }
        }
    }
}
