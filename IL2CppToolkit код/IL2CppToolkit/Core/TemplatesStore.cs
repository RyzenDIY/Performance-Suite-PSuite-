// language: C#, file: Core/TemplatesStore.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ChainTemplate
{
    public string Id;           // "LocalPlayer.health"
    public string GameId;
    public string Name;         // "LocalPlayer → health"
    public string Description;
    public long BaseRva;      // RVA від ModuleBase (0 = module base)
    public List<int> Offsets = new();
    public string ValueType;    // "Single", "String", "Pointer"
    public string Notes;
}

public class TemplatesCollection
{
    public string GameId;
    public List<ChainTemplate> Templates = new();

    public ChainTemplate Find(string id)
    {
        foreach (var t in Templates) if (t.Id == id) return t;
        return null;
    }

    public void Upsert(ChainTemplate t)
    {
        for (int i = 0; i < Templates.Count; i++)
            if (Templates[i].Id == t.Id) { Templates[i] = t; return; }
        Templates.Add(t);
    }

    public void Remove(string id) => Templates.RemoveAll(x => x.Id == id);

    public static TemplatesCollection Load(string gameId)
    {
        string path = Path.Combine(Paths.Root, "templates", gameId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return JsonStore.Load(path, () => new TemplatesCollection { GameId = gameId });
    }

    public void Save(string gameId)
    {
        string path = Path.Combine(Paths.Root, "templates", gameId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        JsonStore.Save(path, this);
    }
}