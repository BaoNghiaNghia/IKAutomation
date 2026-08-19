namespace IK_Auto_ADB.Core.Vision
{
    public interface ITemplateRegistry
    {
        TemplateDefinition GetDefinition(TemplateId id);

        string GetPath(TemplateId id);

        byte[] LoadBytes(TemplateId id);

        bool Exists(TemplateId id);
    }
}
