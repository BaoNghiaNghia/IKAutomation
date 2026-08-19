namespace ADB_Tool_Automation_Post_FB.Core.GameDetection
{
    public sealed class GameStateDetectionContext
    {
        public GameStateDetectionContext(GameState? expectedState, GameState? lastKnownState)
        {
            ExpectedState = expectedState;
            LastKnownState = lastKnownState;
        }

        public GameState? ExpectedState { get; }
        public GameState? LastKnownState { get; }
    }
}
