// language: C#, file: Domain/Label.cs
using System;
using System.Collections.Generic;

public class LabelAlias
{
    public string Class { get; set; }
    public string Field { get; set; }
    public int Offset { get; set; }
    public string Type { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}

public class SemLabel
{
    public string Id { get; set; }
    public string GameId { get; set; }
    public string Semantic { get; set; }
    public string Class { get; set; }
    public string Field { get; set; }
    public int Offset { get; set; }     // для instance: offset; для chain: slot offset
    public string Type { get; set; }
    public bool IsStatic { get; set; }
    public string Description { get; set; }
    public DateTime Verified { get; set; } = DateTime.UtcNow;
    public List<LabelAlias> Aliases { get; set; } = new();

    // ─── NEW: chain для статичних полів
    // якщо Chain == null — звичайна instance-мітка (Offset напряму)
    // якщо Chain != null — це статик через ланцюг:
    //   module + ClassRva → ptr (klass)
    //                     → read +0xB8 (static_fields)
    //                     → read +Chain[i] послідовно
    public long ClassRva { get; set; }
    public List<int> Chain { get; set; }

    public bool IsChain => Chain != null && Chain.Count > 0 && ClassRva > 0;

    public void PushAlias()
    {
        if (string.IsNullOrEmpty(Field)) return;
        Aliases.Add(new LabelAlias { Class = Class, Field = Field, Offset = Offset, Type = Type });
        while (Aliases.Count > 10) Aliases.RemoveAt(0);
    }

    public override string ToString()
        => IsChain
            ? $"{Semantic} ← chain(0x{ClassRva:X}, {string.Join(", ", Chain.ConvertAll(x => "0x" + x.ToString("X")))})"
            : $"{Semantic} ← {Class}.{Field} @ +0x{Offset:X}";
}

public class LabelCollection
{
    public string GameId { get; set; }
    public List<SemLabel> Labels { get; set; } = new();

    public SemLabel FindBySemantic(string semantic)
    {
        foreach (var l in Labels)
            if (l.Semantic == semantic) return l;
        return null;
    }

    public void Upsert(SemLabel l)
    {
        for (int i = 0; i < Labels.Count; i++)
            if (Labels[i].Semantic == l.Semantic) { Labels[i] = l; return; }
        Labels.Add(l);
    }

    public void Remove(string semantic) => Labels.RemoveAll(x => x.Semantic == semantic);

    public static LabelCollection Load(string gameId)
        => JsonStore.Load(Paths.LabelsFile(gameId), () => new LabelCollection { GameId = gameId });

    public void Save() => JsonStore.Save(Paths.LabelsFile(GameId), this);
}