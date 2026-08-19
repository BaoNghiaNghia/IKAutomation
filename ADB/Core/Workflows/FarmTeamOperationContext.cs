using IK_Auto_ADB.Core.TeamSelection;
using System;

namespace IK_Auto_ADB.Core.Workflows
{
    // A gather operation is bound to one fresh WorldMap roster observation.  This
    // is intentionally immutable so lower layers cannot silently replace the
    // ready team with a priority or visual-selection fallback.
    public sealed class FarmTeamOperationContext
    {
        public FarmTeamOperationContext(TeamNumber expectedTeam, Guid rosterScanId,
            DateTimeOffset rosterCapturedAt, string rosterSource)
        {
            ExpectedTeam = expectedTeam;
            RosterScanId = rosterScanId;
            RosterCapturedAt = rosterCapturedAt;
            RosterSource = rosterSource ?? string.Empty;
        }

        public TeamNumber ExpectedTeam { get; private set; }
        public Guid RosterScanId { get; private set; }
        public DateTimeOffset RosterCapturedAt { get; private set; }
        public string RosterSource { get; private set; }
    }
}
