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
            var navigation = WorldMapNavigationServiceFactory.CreateFromAppConfig();
            var levelFallback = ResourceLevelFallbackServiceFactory.CreateFromAppConfig();
            var popup = ResourcePopupVerificationServiceFactory.CreateFromAppConfig();
            var openTeam = OpenTeamSelectionServiceFactory.CreateFromAppConfig();
            var selectTeam = SelectFarmTeamServiceFactory.CreateFromAppConfig();
            var dispatch = DispatchSelectedTeamServiceFactory.CreateFromAppConfig();
            var fallbackOptions = AppConfigResourceFarmFallbackOptionsProvider.Load();
            var profiles = new ResourceTemplateProfileProvider(registry);
            var resourceFallback = new ResourceFarmFallbackService(navigation, levelFallback,
                (ADB_Tool_Automation_Post_FB.Core.ResourcePopup.IResourceAwarePopupVerificationService)popup,
                openTeam, selectTeam, dispatch, profiles,
                fallbackOptions, logger);
            var inner = new OneShotFarmWorkflow(navigation, levelFallback, popup,
                openTeam, selectTeam, dispatch, detector,
                DeviceOperationLock.Shared, workflowOptions,
                new OneShotFarmDiagnosticService(client, workflowOptions.ScreenshotDirectory), logger,
                fallbackOptions, resourceFallback, profiles, registry, new SystemRandomProvider());
            var availability = new WorldMapTeamAvailabilityService(navigation,
                detector, client, registry, matcher, DeviceOperationLock.Shared,
                AppConfigWorldMapTeamAvailabilityOptionsProvider.Load(), logger);
            return new ReadyTeamOneShotFarmWorkflow(inner, availability,
                AppConfigReadyTeamGateOptionsProvider.Load(), logger);
        }
    }
}
