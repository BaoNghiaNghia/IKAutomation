using IK_Auto_ADB.Core.Concurrency;
using IK_Auto_ADB.Core.Fruit2048;
using IK_Auto_ADB.Infrastructure.Concurrency;
using IK_Auto_ADB.Infrastructure.Diagnostics;
using IK_Auto_ADB.Infrastructure.LDPlayer;
using IK_Auto_ADB.Infrastructure.Vision;
using IK_Auto_ADB.Core.Vision;
using System.Drawing;
using System.IO;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
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
        public Fruit2048LearningDiagnosticStore LearningDiagnosticStore { get; set; }
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
            var diagnosticStore = new Fruit2048LearningDiagnosticStore();
            var navigation = new Fruit2048NavigationService(player, player, matcher, catalog,
                new Fruit2048ScreenProfile(), logger, diagnosticStore);
            var proofStore = new Fruit2048LearningProofStore();
            var service = new Fruit2048AutomationService(player, reader,
                new Fruit2048Solver(), new Fruit2048SwipeExecutor(player), ownership,
                // The transition has already verified that this is the merge
                // destination. Allow the learner the same small settling drift
                // accepted by the runtime gate so Tier 5+ candidates are saved.
                new Fruit2048TransitionLearner(learningCatalog, 18, learningCoordinator), learningCatalog, logger, navigation, learningCoordinator,
                new Fruit2048TransitionValidator(), diagnosticStore, proofStore);
            var supervisor = new Fruit2048RuntimeSupervisor(service, logger);
            var calibration = new Fruit2048SeedCalibrationService(player, matcher, catalog,
                new Fruit2048ScreenProfile(), learningCatalog, ownership, logger, navigation);
            return new Fruit2048Feature
            {
                PlayerClient = player,
                AutomationService = supervisor,
                OwnershipService = ownership,
                TemplateCatalog = catalog,
                LearningCatalog = learningCatalog,
                CalibrationService = calibration,
                LearningCoordinator = learningCoordinator,
                LearningDiagnosticStore = diagnosticStore
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
                {
                    int tier = FruitTierCatalog.ToTier(value);
                    FruitTileVisualFingerprint fingerprint = Fruit2048CellVisualNormalizer.CreateFingerprint(bitmap,
                        new ImageRegion(0, 0, bitmap.Width, bitmap.Height));
                    if (value == 0 || value == 1)
                    {
                        // Empty/Tier1 are bootstrap seeds. Replace stale static
                        // fingerprints from earlier board captures on startup.
                        learning.ReplaceBootstrapSeed(tier, fingerprint,
                            value == 0 ? templates.EmptySeedPath : templates.Tier1SeedPath);
                    }
                    else learning.AddSeed(tier, fingerprint);
                }
            }
        }
    }
}
