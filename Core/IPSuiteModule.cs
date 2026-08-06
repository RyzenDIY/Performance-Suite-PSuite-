namespace PSuite.Core
{
    public interface IPSuiteModule
    {
        ModuleDetectResult Detect();
        ModuleApplyResult Apply(string capturePath);
        ModuleRollbackResult Rollback(string capturePath);
        ModuleDetectResult? Verify();
    }
}