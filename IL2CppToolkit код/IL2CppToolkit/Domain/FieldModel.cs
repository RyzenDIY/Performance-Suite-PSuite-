// language: C#, file: Domain/FieldModel.cs
public class FieldModel
{
    public string Name { get; set; }
    public string Type { get; set; }    // "System.Single", "UnityEngine.Vector3"
    public int Offset { get; set; }
    public bool IsStatic { get; set; }

    public override string ToString()
        => $"{Name} : {Type} @ +0x{Offset:X}";
}