using System.IO;

namespace PSuite.Core
{
    // A manifest plus the folder it was loaded from — needed to resolve
    // relative script paths (detect.ps1 etc.) to absolute ones.
    public class InstalledModule
    {
        public ModuleManifest Manifest { get; }
        public string FolderPath { get; }

        public InstalledModule(ModuleManifest manifest, string folderPath)
        {
            Manifest = manifest;
            FolderPath = folderPath;
        }

        public string ResolvePath(string relativeFileName) => Path.Combine(FolderPath, relativeFileName);
    }
}