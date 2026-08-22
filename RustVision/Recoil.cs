using System;
using System.Runtime.InteropServices;

public class Recoil
{
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, uint dwExtraInfo);
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    // Векторна матриця за твоєю мішенню: спочатку сильний підкид вгору, потім убік фундамента [2.3]
    private static readonly int[,] Ak47Pattern = new int[,]
    {
        { -2, 5 }, { -2, 5 }, { -3, 6 }, { -2, 5 }, { -3, 5 },  // 1-5 постріли (АК рве вгору)
        { -4, 4 }, { -4, 3 }, { -3, 3 }, { -2, 2 }, { -1, 2 },  // 6-10 постріли (АК веде вліво за стіну)
        {  2, 3 }, {  3, 3 }, {  2, 2 }, {  1, 2 }, {  0, 2 }   // 11-15 постріли (S-подібна крива)
    };

    public static void Compensate(int shotIndex, float powerMultiplier)
    {
        if (shotIndex < 0) return;

        int index = Math.Min(shotIndex, Ak47Pattern.GetLength(0) - 1);

        float baseComponentsX = Ak47Pattern[index, 0];
        float baseComponentsY = Ak47Pattern[index, 1];

        // Сила No-Recoil регулюється повзунком з меню під твою чутливість
        int moveX = (int)Math.Round(baseComponentsX * powerMultiplier);
        int moveY = (int)Math.Round(baseComponentsY * powerMultiplier);

        mouse_event(MOUSEEVENTF_MOVE, moveX, moveY, 0, 0);
    }
}
