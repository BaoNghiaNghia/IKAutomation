namespace IK_Auto_ADB.Core.ResourceSearch
{
    public enum ResourceSearchFailureReason
    {
        None,
        SearchOtherRegion,
        TargetLevelTooLow,
        ResourceAreaLv2Redirect,
        ResourceAreaLv2PointAttemptsExhausted,
        ResourceToastUnclassified,
        ResourceAreaLv2TemplateUnavailable,
        ResourceToastProbeLate,
        ResourceToastCaptureUnavailable,
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
