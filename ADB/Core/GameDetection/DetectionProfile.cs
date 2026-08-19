using IK_Auto_ADB.Core.Vision;
using System;
using System.Collections.Generic;

namespace IK_Auto_ADB.Core.GameDetection
{
    public sealed class DetectionProfile
    {
        public DetectionProfile(IReadOnlyList<TemplateId> primaryTemplates,
            IReadOnlyList<TemplateId> transitionTemplates)
        {
            PrimaryTemplates = primaryTemplates ?? throw new ArgumentNullException(nameof(primaryTemplates));
            TransitionTemplates = transitionTemplates ?? throw new ArgumentNullException(nameof(transitionTemplates));
        }

        public IReadOnlyList<TemplateId> PrimaryTemplates { get; }
        public IReadOnlyList<TemplateId> TransitionTemplates { get; }
    }
}
