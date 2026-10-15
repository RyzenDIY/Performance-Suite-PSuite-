// language: C#, file: Services/LabelService.cs
using System;
using System.Collections.Generic;
using System.Linq;

public static class LabelService
{
    public static readonly string[] PresetSemantics =
    {
        "health", "maxHealth", "displayName", "position", "rotation",
        "velocity", "playerFlags", "team", "steamId", "userId",
        "viewMatrix", "clientEntities", "vals", "baseTransform",
        "isLocalPlayer", "isSleeping", "isWounded",
        "activeItem", "clothing", "localPlayer", "main", "custom...",
    };

    public static LabelCollection Load(string gameId) => LabelCollection.Load(gameId);

    // ─── instance-мітка (як було)
    public static bool Add(string gameId, string semantic, string cls, string field,
                          int offset, string type, bool isStatic, string description)
    {
        if (string.IsNullOrWhiteSpace(semantic)) return false;
        if (string.IsNullOrWhiteSpace(cls)) return false;
        if (string.IsNullOrWhiteSpace(field)) return false;

        var col = LabelCollection.Load(gameId);
        col.Upsert(new SemLabel
        {
            Id = $"{gameId}.{cls}.{semantic}",
            GameId = gameId,
            Semantic = semantic.Trim(),
            Class = cls.Trim(),
            Field = field.Trim(),
            Offset = offset,
            Type = type,
            IsStatic = isStatic,
            Description = description ?? "",
            Verified = DateTime.UtcNow,
        });
        col.Save();
        return true;
    }

    // ─── NEW: chain-мітка (для статиків)
    public static bool AddChain(string gameId, string semantic, string cls, string field,
                                 long classRva, List<int> chain, string type, string description)
    {
        if (string.IsNullOrWhiteSpace(semantic)) return false;
        if (string.IsNullOrWhiteSpace(cls)) return false;
        if (classRva <= 0 || chain == null || chain.Count == 0) return false;

        var col = LabelCollection.Load(gameId);
        col.Upsert(new SemLabel
        {
            Id = $"{gameId}.{cls}.{semantic}",
            GameId = gameId,
            Semantic = semantic.Trim(),
            Class = cls.Trim(),
            Field = field.Trim(),
            Offset = chain[chain.Count - 1],
            Type = type,
            IsStatic = true,
            ClassRva = classRva,
            Chain = new List<int>(chain),
            Description = description ?? "",
            Verified = DateTime.UtcNow,
        });
        col.Save();
        return true;
    }

    public static bool Remove(string gameId, string semantic)
    {
        var col = LabelCollection.Load(gameId);
        col.Remove(semantic);
        col.Save();
        return true;
    }

    public static List<SemLabel> GetAll(string gameId)
    {
        var col = LabelCollection.Load(gameId);
        return col.Labels.OrderBy(x => x.Semantic).ToList();
    }

    public static string GuessSemantic(string fieldName, string type)
    {
        string f = (fieldName ?? "").ToLowerInvariant();
        if (f.Contains("health") || f == "_hp") return "health";
        if (f.Contains("display") || f.Contains("nickname")) return "displayName";
        if (f.Contains("flag")) return "playerFlags";
        if (f == "position" || f == "pos") return "position";
        if (f.Contains("rotation") || f == "rot") return "rotation";
        if (f.Contains("velocity")) return "velocity";
        if (f.Contains("team")) return "team";
        if (f.Contains("steamid")) return "steamId";
        if (f.Contains("viewmatrix") || f.Contains("worldtocamera")) return "viewMatrix";
        if (f.Contains("transform")) return "baseTransform";
        if (f.Contains("entities")) return "clientEntities";
        if (f.Contains("localsleep") || f.Contains("sleeping")) return "isSleeping";
        if (f.Contains("wounded")) return "isWounded";
        if (f.Contains("maxhealth")) return "maxHealth";
        if (f.Contains("localplayer") || f == "main") return "localPlayer";
        return "custom...";
    }
}