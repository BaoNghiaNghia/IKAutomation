using System;

namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    public sealed class TemplateDefinition
    {
        public TemplateDefinition(TemplateId id, string relativePath, double defaultThreshold)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Template relative path is required.", nameof(relativePath));

            if (defaultThreshold < 0 || defaultThreshold > 1)
                throw new ArgumentOutOfRangeException(nameof(defaultThreshold), "Threshold must be between 0 and 1.");

            Id = id;
            RelativePath = relativePath;
            DefaultThreshold = defaultThreshold;
        }

        public TemplateId Id { get; }

        public string RelativePath { get; }

        public double DefaultThreshold { get; }
    }
}
