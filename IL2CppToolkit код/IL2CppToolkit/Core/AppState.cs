// language: C#, file: Core/AppState.cs
using System;

public static class AppState
{
    public static Game ActiveGame { get; private set; }
    public static Dump CurrentDump { get; private set; }
    public static string CurrentSnapshotPath { get; private set; }

    public static event Action GameChanged;
    public static event Action DumpChanged;

    public static void SetGame(Game g)
    {
        ActiveGame = g;
        GameChanged?.Invoke();
    }

    public static void SetDump(Dump d, string path)
    {
        CurrentDump = d;
        CurrentSnapshotPath = path;
        DumpChanged?.Invoke();
    }

    public static void ClearDump()
    {
        CurrentDump = null;
        CurrentSnapshotPath = null;
        DumpChanged?.Invoke();
    }
}