namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public enum ResourceSearchOutcome
    {
        ResourceNotFound,
        ResourceAreaLv2Redirect,
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
