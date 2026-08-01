namespace ADB_Tool_Automation_Post_FB.Core.ResourceSearch
{
    public enum ResourceSearchOutcome
    {
        ResourceNotFound,
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
