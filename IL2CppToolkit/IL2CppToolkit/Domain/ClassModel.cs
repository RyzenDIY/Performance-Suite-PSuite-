// language: C#, file: Domain/ClassModel.cs
using System.Collections.Generic;

public class ClassModel
{
    public string Name { get; set; }
    public string Namespace { get; set; }
    public string Parent { get; set; }
    public int TypeDefIndex { get; set; }
    public long StaticRva { get; set; }        // RVA Il2CppClass* (якщо відомо)
    public List<FieldModel> Fields { get; set; } = new();
    public List<MethodModel> Methods { get; set; } = new();

    public FieldModel FindField(string name)
    {
        foreach (var f in Fields)
            if (f.Name == name) return f;
        return null;
    }

    public FieldModel FindFieldByOffset(int offset)
    {
        foreach (var f in Fields)
            if (f.Offset == offset) return f;
        return null;
    }

    public override string ToString() => $"{Name} ({Fields.Count} fields, {Methods.Count} methods)";
}

public class MethodModel
{
    public string Name { get; set; }
    public string ReturnType { get; set; }
    public long Rva { get; set; }           // RVA точки входу
    public int ParamCount { get; set; }
    public List<string> ParamTypes { get; set; } = new();
}