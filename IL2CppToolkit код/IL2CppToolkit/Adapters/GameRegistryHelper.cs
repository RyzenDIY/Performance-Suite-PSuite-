// language: C#, file: Adapters/GameRegistryHelper.cs
using System;
using System.IO;
using System.Linq;

public static class GameRegistryHelper
{
    // сканує всі відомі ігри, додає ті що знайдені
    public static int AutoDetectAll()
    {
        var reg = GameRegistry.Load();
        int added = 0;

        foreach (var known in SteamFinder.KnownGames())
        {
            if (reg.Find(known.id) != null) continue;

            string folder = SteamFinder.FindGameFolder(known.appId);
            if (folder == null) continue;

            string ga = Path.Combine(folder, known.module);
            if (!File.Exists(ga)) continue;

            // шукаємо metadata у типових місцях
            string[] mdPaths =
            {
                Path.Combine(folder, $"{known.process}_Data", "il2cpp_data", "Metadata", "global-metadata.dat"),
                Path.Combine(folder, "RustClient_Data",       "il2cpp_data", "Metadata", "global-metadata.dat"),
                Path.Combine(folder, "Among Us_Data",         "il2cpp_data", "Metadata", "global-metadata.dat"),
            };
            string md = mdPaths.FirstOrDefault(File.Exists);

            var game = new Game
            {
                Id = known.id,
                Name = known.name,
                Process = known.process,
                Module = known.module,
                InstallDir = folder,
                ExecutablePath = ga,
                MetadataPath = md,
                Launcher = known.launcher,
                SteamAppId = known.appId,
                LastSeenAt = DateTime.UtcNow
            };

            reg.Upsert(game);
            GameFolders.EnsureStructure(game);
            added++;
        }

        if (added > 0) reg.Save();
        return added;
    }

    // викликається при додаванні однієї гри — одразу створює папки
    public static void RegisterAndPrepare(Game game)
    {
        var reg = GameRegistry.Load();
        reg.Upsert(game);
        reg.Save();
        GameFolders.EnsureStructure(game);
    }
}