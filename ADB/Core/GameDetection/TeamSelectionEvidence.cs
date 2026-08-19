using IK_Auto_ADB.Core.Vision;
using System.Linq;

namespace IK_Auto_ADB.Core.GameDetection
{
    public static class TeamSelectionEvidence
    {
        public static bool IsConfirmed(GameDetectionResult state)
        {
            return state != null && state.IsSuccessful
                && state.State == GameState.TeamSelection
                && Found(state, TemplateId.TeamSelectionPanelAnchor)
                && (Found(state, TemplateId.TeamAdjustFormationButton)
                    || Found(state, TemplateId.TeamActionButtonEnabled));
        }

        public static bool IsConfirmed(System.Collections.Generic.IReadOnlyList<GameDetectionEvidence> evidence)
        {
            return evidence != null
                && Found(evidence, TemplateId.TeamSelectionPanelAnchor)
                && (Found(evidence, TemplateId.TeamAdjustFormationButton)
                    || Found(evidence, TemplateId.TeamActionButtonEnabled));
        }

        private static bool Found(GameDetectionResult state, TemplateId id) =>
            Found(state.Evidence, id);

        private static bool Found(System.Collections.Generic.IReadOnlyList<GameDetectionEvidence> evidence,
            TemplateId id) => evidence.Any(item => item.TemplateId == id && item.Found);
    }
}
