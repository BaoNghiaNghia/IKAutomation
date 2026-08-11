using ADB_Tool_Automation_Post_FB.Core.Concurrency;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Infrastructure.Concurrency;
using ADB_Tool_Automation_Post_FB.Infrastructure.Diagnostics;
using ADB_Tool_Automation_Post_FB.Infrastructure.LDPlayer;
using ADB_Tool_Automation_Post_FB.Infrastructure.Vision;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System.Drawing;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048Feature
    {
        public AutoLdPlayerClient PlayerClient { get; set; }
        public IFruit2048AutomationService AutomationService { get; set; }
        public IDeviceAutomationOwnershipService OwnershipService { get; set; }
        public Fruit2048TemplateCatalog TemplateCatalog { get; set; }
        public IFruitTileLearningCatalog LearningCatalog { get; set; }
        public IFruit2048SeedCalibrationService CalibrationService { get; set; }
        public IFruit2048LearningCoordinator LearningCoordinator { get; set; }
    }

    public static class Fruit2048FeatureFactory
    {
        public static Fruit2048Feature Create()
        {
            var logger = new ApplicationDiagnosticLogger();
            var player = new AutoLdPlayerClient();
            var matcher = new KAutoImageMatcher();
            var catalog = new Fruit2048TemplateCatalog();
            Fruit2048SeedAvailability seeds = catalog.SeedAvailability;
            logger.Info($"[Fruit2048 Seed Assets] SourceEmptyPath='{Fruit2048TemplateCatalog.RelativeDirectory}\\{Fruit2048TemplateCatalog.EmptyTile}', "
                + $"RuntimeEmptyPath='{seeds.EmptyPath}', EmptyAvailable={seeds.EmptyAvailable}, "
                + $"SourceTier1Path='{Fruit2048TemplateCatalog.RelativeDirectory}\\{Fruit2048TemplateCatalog.Tier1Tile}', "
                + $"RuntimeTier1Path='{seeds.Tier1Path}', Tier1Available={seeds.Tier1Available}");
            logger.Info($"[Fruit2048 Seed] Resolution='1280x720', Seed='Empty', Source='{seeds.EmptySource}', Path='{seeds.EmptyPath}', Ready={seeds.EmptyAvailable}");
            logger.Info($"[Fruit2048 Seed] Resolution='1280x720', Seed='Tier1', Source='{seeds.Tier1Source}', Path='{seeds.Tier1Path}', Ready={seeds.Tier1Available}");
            var learningCatalog = new FruitTileLearningCatalog(null, logger);
            var learningCoordinator = new Fruit2048LearningCoordinator(learningCatalog, logger);
            ImportStaticSeeds(catalog, learningCatalog);
            var ownership = DeviceAutomationOwnershipService.Shared;
            ownership.SetLogger(logger);
            var reader = new Fruit2048BoardReader(player, player, matcher, catalog,
                new Fruit2048ScreenProfile(), learningCatalog, logger);
            var navigation = new Fruit2048NavigationService(player, player, matcher, catalog,
                new Fruit2048ScreenProfile(), logger);
            var service = new Fruit2048AutomationService(player, reader,
                new Fruit2048Solver(), new Fruit2048SwipeExecutor(player), ownership,
                new Fruit2048TransitionLearner(learningCatalog, 5, learningCoordinator), learningCatalog, logger, navigation, learningCoordinator);
            var calibration = new Fruit2048SeedCalibrationService(player, matcher, catalog,
                new Fruit2048ScreenProfile(), learningCatalog, ownership, logger, navigation);
            return new Fruit2048Feature
            {
                PlayerClient = player,
                AutomationService = service,
                OwnershipService = ownership,
                TemplateCatalog = catalog,
                LearningCatalog = learningCatalog,
                CalibrationService = calibration,
                LearningCoordinator = learningCoordinator
            };
        }

        private static void ImportStaticSeeds(Fruit2048TemplateCatalog templates,
            FruitTileLearningCatalog learning)
        {
            foreach (int value in templates.AvailableSeedValues)
            {
                byte[] png;
                if (!templates.TryGetTile(value, out png)) continue;
                using (var stream = new MemoryStream(png, false))
                using (var bitmap = new Bitmap(stream))
                    learning.AddSeed(FruitTierCatalog.ToTier(value),
                        FruitTileFingerprint.Create(bitmap,
                            new ImageRegion(0, 0, bitmap.Width, bitmap.Height)));
            }
        }
    }
}
