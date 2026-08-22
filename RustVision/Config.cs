using System;
using System.IO;

public static class Config
{
    private static string configPath = "config.ini";

    public static void Save()
    {
        try
        {
            using (StreamWriter sw = new StreamWriter(configPath))
            {
                sw.WriteLine($"aimFov={Program.aimFov}");
                sw.WriteLine($"smoothness={Program.smoothness}");
                sw.WriteLine($"modelResolution={Program.modelResolution}");
                sw.WriteLine($"maxSoftwareFps={Program.maxSoftwareFps}");
                sw.WriteLine($"recoilPower={Program.recoilPower}");
                sw.WriteLine($"aimEnabled={Program.aimEnabled}");
                sw.WriteLine($"noRecoilEnabled={Program.noRecoilEnabled}");
                sw.WriteLine($"antiScreenshotEnabled={Program.antiScreenshotEnabled}");
                sw.WriteLine($"aimBoneMode={Aimbot.aimBoneMode}");
                sw.WriteLine($"aimKey={Program.aimKey}");
                sw.WriteLine($"toggleKey={Program.toggleKey}");
            }
        }
        catch { }
    }

    public static void Load()
    {
        if (!File.Exists(configPath)) return;
        try
        {
            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                string[] parts = line.Split('=');
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string val = parts[1].Trim();

                if (key == "aimFov") Program.aimFov = int.Parse(val);
                else if (key == "smoothness") Program.smoothness = float.Parse(val);
                else if (key == "modelResolution") Program.modelResolution = int.Parse(val);
                else if (key == "maxSoftwareFps") Program.maxSoftwareFps = int.Parse(val);
                else if (key == "recoilPower") Program.recoilPower = float.Parse(val);
                else if (key == "aimEnabled") Program.aimEnabled = bool.Parse(val);
                else if (key == "noRecoilEnabled") Program.noRecoilEnabled = bool.Parse(val);
                else if (key == "antiScreenshotEnabled") Program.antiScreenshotEnabled = bool.Parse(val);
                else if (key == "aimBoneMode") Aimbot.aimBoneMode = int.Parse(val);
                else if (key == "aimKey") Program.aimKey = int.Parse(val);
                else if (key == "toggleKey") Program.toggleKey = int.Parse(val);
            }
        }
        catch { }
    }
}
