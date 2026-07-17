using ADB_Tool_Automation_Post_FB.Core.Workflows;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.GameDetection;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.MarchDispatch;
using ADB_Tool_Automation_Post_FB.Infrastructure.Navigation;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup;
using ADB_Tool_Automation_Post_FB.Infrastructure.ResourceSearch;
using ADB_Tool_Automation_Post_FB.Infrastructure.TeamSelection;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Workflows
{
    public static class OneShotFarmWorkflowFactory
    {
        public static IOneShotFarmWorkflow CreateFromAppConfig()
        {
            var client = new AutoLdPlayerClient(); var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher(); var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            var workflowOptions = AppConfigOneShotFarmWorkflowOptionsProvider.Load();
            return new OneShotFarmWorkflow(WorldMapNavigationServiceFactory.CreateFromAppConfig(),
                ResourceLevelFallbackServiceFactory.CreateFromAppConfig(),
                ResourcePopupVerificationServiceFactory.CreateFromAppConfig(),
                OpenTeamSelectionServiceFactory.CreateFromAppConfig(),
                SelectFarmTeamServiceFactory.CreateFromAppConfig(),
                DispatchSelectedTeamServiceFactory.CreateFromAppConfig(), detector,
                DeviceOperationLock.Shared, workflowOptions,
                new OneShotFarmDiagnosticService(client, workflowOptions.ScreenshotDirectory), logger,
                AppConfigResourceFarmFallbackOptionsProvider.Load());
        }
    }
}
