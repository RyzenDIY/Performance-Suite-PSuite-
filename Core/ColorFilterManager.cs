using System;
using System.Runtime.InteropServices;

namespace PSuite.Core
{
    // A real, system-wide color adjustment using the Windows Gamma Ramp
    // API (gdi32 SetDeviceGammaRamp) — the same low-level mechanism apps
    // like f.lux use for Night Light-style effects.
    //
    // Honest technical limit, stated plainly instead of hidden: a gamma
    // ramp is three independent per-channel lookup tables (R, G, B each
    // mapped 0..255 → 0..255 on their own). That can genuinely do
    // brightness, contrast and gamma/tint. It CANNOT do real saturation
    // adjustment — saturation requires mixing all three channels together
    // per pixel (e.g. an HSV transform), which a 1D-per-channel LUT is
    // mathematically unable to express. Anything claiming "saturation"
    // through this API alone would just be contrast/tint wearing a
    // different label. Real per-game sharpness/saturation (what NVIDIA
    // Freestyle or ReShade do) requires hooking the game's renderer —
    // a fundamentally different, much larger piece of software.
    //
    // Also not permanent: the gamma ramp is a per-session GPU/display
    // setting. It resets on reboot/logon and can be overridden by other
    // apps (fullscreen games, monitor OSD/hardware changes, other
    // gamma-ramp tools). PSuite reapplies the saved values each time it
    // starts, if the feature is enabled — it's not a system-level install.
    public static class ColorFilterManager
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GammaRamp
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Blue;
        }

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref GammaRamp lpRamp);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        // brightness: -100..100 (offset)
        // contrastPercent: 50..200 (100 = no change)
        // gamma: 0.5..2.5 (1.0 = no change)
        // Returns false (with no exception) if Windows rejects the ramp —
        // some GPU drivers refuse gamma-ramp changes entirely (common on
        // laptops with certain panel/driver combos), which is a real,
        // known limitation, not a PSuite bug.
        public static bool Apply(int brightness, int contrastPercent, double gamma)
        {
            var hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;

            try
            {
                var ramp = BuildRamp(brightness, contrastPercent, gamma);
                return SetDeviceGammaRamp(hdc, ref ramp);
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        public static bool ResetToDefault() => Apply(0, 100, 1.0);

        private static GammaRamp BuildRamp(int brightness, int contrastPercent, double gamma)
        {
            var ramp = new GammaRamp { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };

            var contrast = Math.Clamp(contrastPercent, 50, 200) / 100.0;
            var gammaExponent = 1.0 / Math.Clamp(gamma, 0.5, 2.5);
            var brightnessOffset = Math.Clamp(brightness, -100, 100) / 255.0;

            for (int i = 0; i < 256; i++)
            {
                double v = i / 255.0;
                v = Math.Pow(v, gammaExponent);          // gamma
                v = (v - 0.5) * contrast + 0.5;           // contrast around midpoint
                v += brightnessOffset;                    // brightness
                v = Math.Clamp(v, 0.0, 1.0);

                var value = (ushort)Math.Round(v * 65535.0);
                ramp.Red[i] = value;
                ramp.Green[i] = value;
                ramp.Blue[i] = value;
            }

            return ramp;
        }
    }
}
