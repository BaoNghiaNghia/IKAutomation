namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public enum ResourceSearchOutcome
    {
        ResourceNotFound,
        ResourceAreaLv2Redirect,
        ResourceToastUnclassified,
        ResourceAreaLv2TemplateUnavailable,
        ResourceToastProbeLate,
        ResourceToastCaptureUnavailable,
        ResourceLocated,
        SearchTapNotApplied,
        SearchButtonUnavailable,
        SearchPanelUnexpectedlyClosed,
        SearchTransitionTimeout,
        TechnicalFailure,
        Timeout,
        Failed,
        Cancelled
    }
}
