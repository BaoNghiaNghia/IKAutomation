using IK_Auto_ADB.Core.ResourceSearch;
using IK_Auto_ADB.Core.Workflows;
using IK_Auto_ADB.Core.TeamSelection;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.GameDetection;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.MarchDispatch;
using IK_Auto_ADB.Infrastructure.Navigation;
using IK_Auto_ADB.Infrastructure.ResourcePopup;
using IK_Auto_ADB.Infrastructure.ResourceSearch;
using IK_Auto_ADB.Infrastructure.TeamSelection;
using IK_Auto_ADB.Infrastructure.Vision;

namespace IK_Auto_ADB.Infrastructure.Workflows
{
    public static class OneShotFarmWorkflowFactory
    {
        public static IWorldMapTeamAvailabilityService CreateTeamAvailabilityFromAppConfig()
        {
            var client = new AutoLdPlayerClient();
            var registry = new TemplateRegistry();
            var matcher = new KAutoImageMatcher();
            var logger = new ApplicationDiagnosticLogger();
            var detectionOptions = AppConfigGameDetectionOptionsProvider.Load();
            var detector = new GameStateDetector(client, registry, matcher, detectionOptions,
                new UnknownScreenshotStore(detectionOptions.UnknownScreenshotDirectory), logger);
            return new WorldMapTeamAvailabilityService(
                WorldMapNavigationServiceFactory.CreateFromAppConfig(), detector,
                client, registry, matcher, DeviceOperationLock.Shared,
                AppConfigWorldMapTeamAvailabilityOptionsProvider.Load(), logger);
        }

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
            var areaLv2Selector = new ResourceAreaLv2PointSelector();
            var areaLv2Recovery = new ResourceAreaLv2RecoveryCoordinator(
                navigation, areaLv2Selector, client, logger);
            var resourceFallback = new ResourceFarmFallbackService(navigation, levelFallback,
                (IK_Auto_ADB.Core.ResourcePopup.IResourceAwarePopupVerificationService)popup,
                openTeam, selectTeam, dispatch, profiles,
                fallbackOptions, logger, areaLv2Recovery);
            var inner = new OneShotFarmWorkflow(navigation, levelFallback, popup,
                openTeam, selectTeam, dispatch, detector,
                DeviceOperationLock.Shared, workflowOptions,
                new OneShotFarmDiagnosticService(client, workflowOptions.ScreenshotDirectory,
                    AppConfigOneShotFarmWorkflowOptionsProvider.LoadDiagnosticOptions(), null, null), logger,
                fallbackOptions, resourceFallback, profiles, registry, new SystemRandomProvider());
            var availability = new WorldMapTeamAvailabilityService(navigation,
                detector, client, registry, matcher, DeviceOperationLock.Shared,
                AppConfigWorldMapTeamAvailabilityOptionsProvider.Load(), logger);
            return new ReadyTeamOneShotFarmWorkflow(inner, availability,
                AppConfigReadyTeamGateOptionsProvider.Load(), logger);
        }
    }
}
