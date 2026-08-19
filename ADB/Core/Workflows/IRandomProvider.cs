namespace ADB_Tool_Automation_Post_FB.Core.Workflows
{
    public interface IRandomProvider
    {
        int Next(int maxExclusive);
    }
}
