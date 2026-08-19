namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public interface IResourceTemplateProfileProvider
    {
        ResourceTemplateProfile Get(ResourceType resourceType);
        bool IsSupported(ResourceType resourceType);
        string GetUnsupportedReason(ResourceType resourceType);
    }
}
