namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public enum ResourceSearchFailureReason
    {
        None,
        SearchOtherRegion,
        TargetLevelTooLow,
        ResourceAreaLv2Redirect,
        ResourceAreaLv2PointAttemptsExhausted,
        SeasonMapRestriction,
        SearchTapNotApplied,
        SearchButtonStillVisibleAfterMaxAttempts,
        SearchButtonUnavailable,
        ToastAmbiguous,
        SearchTransitionTimeout,
        ResourceUnavailable,
        TechnicalFailure
    }
}
