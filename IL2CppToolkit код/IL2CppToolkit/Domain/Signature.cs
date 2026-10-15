// language: C#, file: Domain/Signature.cs
using System;
using System.Collections.Generic;

public enum SigPurpose { Static, Function, Vtable, Unknown }
public enum SigResolve { RipRelative, Direct, None }

public class Signature
{
    public string Id { get; set; }    // "rust.BaseNetworkable.static"
    public string GameId { get; set; }
    public string Purpose { get; set; }    // "static" | "function" | "vtable"
    public string Pattern { get; set; }    // "48 8B 05 ?? ?? ?? ??"
    public int OperandOff { get; set; }    // де в інструкції починається rel32
    public SigResolve Resolve { get; set; } = SigResolve.RipRelative;
    public long LastRva { get; set; }    // де знайшлось на останньому патчі
    public DateTime LastMatch { get; set; } = DateTime.UtcNow;
    public string Description { get; set; }

    public override string ToString() => $"{Id}  [{Purpose}]  {Pattern}";
}

public class SignatureCollection
{
    public string GameId { get; set; }
    public List<Signature> Signatures { get; set; } = new();

    public Signature Find(string id)
    {
        foreach (var s in Signatures) if (s.Id == id) return s;
        return null;
    }

    public void Upsert(Signature s)
    {
        for (int i = 0; i < Signatures.Count; i++)
            if (Signatures[i].Id == s.Id) { Signatures[i] = s; return; }
        Signatures.Add(s);
    }

    public static SignatureCollection Load(string gameId)
        => JsonStore.Load(Paths.SignaturesFile(gameId), () => new SignatureCollection { GameId = gameId });

    public void Save() => JsonStore.Save(Paths.SignaturesFile(GameId), this);
}