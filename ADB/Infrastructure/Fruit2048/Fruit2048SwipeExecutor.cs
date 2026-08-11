using ADB_Tool_Automation_Post_FB.Core.Abstractions;
using ADB_Tool_Automation_Post_FB.Core.Fruit2048;
using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048SwipeExecutor : IFruit2048SwipeExecutor
    {
        private readonly ILdPlayerClient playerClient;
        private readonly int swipeDurationMs;
        private readonly int settleDelayMs;

        public Fruit2048SwipeExecutor(ILdPlayerClient playerClient,
            int swipeDurationMs = 160, int settleDelayMs = 500)
        {
            this.playerClient = playerClient ?? throw new ArgumentNullException(nameof(playerClient));
            if (swipeDurationMs < 120 || swipeDurationMs > 200)
                throw new ArgumentOutOfRangeException(nameof(swipeDurationMs));
            if (settleDelayMs < 400 || settleDelayMs > 600)
                throw new ArgumentOutOfRangeException(nameof(settleDelayMs));
            this.swipeDurationMs = swipeDurationMs;
            this.settleDelayMs = settleDelayMs;
        }

        public async Task ExecuteAsync(string deviceName, Fruit2048Move move,
            ImageRegion boardRegion, int screenWidth, int screenHeight,
            CancellationToken cancellationToken)
        {
            double left = (boardRegion.X + (boardRegion.Width * 0.25)) / screenWidth;
            double right = (boardRegion.X + (boardRegion.Width * 0.75)) / screenWidth;
            double top = (boardRegion.Y + (boardRegion.Height * 0.25)) / screenHeight;
            double bottom = (boardRegion.Y + (boardRegion.Height * 0.75)) / screenHeight;
            double centerX = (boardRegion.X + (boardRegion.Width * 0.50)) / screenWidth;
            double centerY = (boardRegion.Y + (boardRegion.Height * 0.50)) / screenHeight;
            double startX, startY, endX, endY;
            switch (move)
            {
                case Fruit2048Move.Left:
                    startX = right; startY = centerY; endX = left; endY = centerY; break;
                case Fruit2048Move.Right:
                    startX = left; startY = centerY; endX = right; endY = centerY; break;
                case Fruit2048Move.Up:
                    startX = centerX; startY = bottom; endX = centerX; endY = top; break;
                case Fruit2048Move.Down:
                    startX = centerX; startY = top; endX = centerX; endY = bottom; break;
                default: throw new ArgumentOutOfRangeException(nameof(move));
            }
            await playerClient.SwipeByPercentAsync(deviceName, startX, startY,
                endX, endY, swipeDurationMs, cancellationToken);
            await Task.Delay(settleDelayMs, cancellationToken);
        }
    }
}
