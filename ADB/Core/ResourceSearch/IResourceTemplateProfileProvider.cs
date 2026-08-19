namespace IK_Auto_ADB.Core.ResourceSearch
{
    public interface IResourceTemplateProfileProvider
    {
        ResourceTemplateProfile Get(ResourceType resourceType);
        bool IsSupported(ResourceType resourceType);
        string GetUnsupportedReason(ResourceType resourceType);
    }
}
