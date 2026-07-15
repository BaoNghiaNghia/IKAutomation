namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public interface ITemplateRegistry
    {
        TemplateDefinition GetDefinition(TemplateId id);

        string GetPath(TemplateId id);

        byte[] LoadBytes(TemplateId id);

        bool Exists(TemplateId id);
    }
}
