using IK_Auto_ADB.Core.Abstractions;
using IK_Auto_ADB.Core.Fruit2048;
using IK_Auto_ADB.Core.Vision;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IK_Auto_ADB.Infrastructure.Fruit2048
{
    public sealed class Fruit2048SwipeExecutor : IFruit2048SwipeExecutor
    {
        private readonly ILdPlayerClient playerClient;
        private readonly int swipeDurationMs;
        private readonly int settleDelayMs;

        public Fruit2048SwipeExecutor(ILdPlayerClient playerClient,
            int swipeDurationMs = 160, int settleDelayMs = 0)
        {
            this.playerClient = playerClient ?? throw new ArgumentNullException(nameof(playerClient));
            if (swipeDurationMs < 120 || swipeDurationMs > 200)
                throw new ArgumentOutOfRangeException(nameof(swipeDurationMs));
            if (settleDelayMs < 0 || settleDelayMs > 600)
                throw new ArgumentOutOfRangeException(nameof(settleDelayMs));
            this.swipeDurationMs = swipeDurationMs;
            this.settleDelayMs = settleDelayMs;
        }

        public async Task<Fruit2048SwipeExecution> ExecuteAsync(string deviceName, Fruit2048Board board,
            Fruit2048Move move, ImageRegion boardRegion, int screenWidth, int screenHeight,
            CancellationToken cancellationToken)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (screenWidth <= 0 || screenHeight <= 0) throw new ArgumentOutOfRangeException();

            // Fruit2048 receives Android/game coordinates.  Always begin from the
            // centre cell of the detected board and keep the whole gesture inside
            // that board.  Starting from a source fruit or using screen percentages
            // made short/incorrect gestures look like swipes from the top-left of
            // the emulator when a board was offset from the screen origin.
            GesturePoints gesture = ResolveGesture(move, boardRegion);
            switch (move)
            {
                case Fruit2048Move.Left:
                case Fruit2048Move.Right:
                case Fruit2048Move.Up:
                case Fruit2048Move.Down:
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(move));
            }
            IAbsoluteSwipeLdPlayerClient absolute = playerClient as IAbsoluteSwipeLdPlayerClient;
            if (absolute != null)
                await absolute.SwipeAsync(deviceName, gesture.StartX, gesture.StartY,
                    gesture.EndX, gesture.EndY, swipeDurationMs, cancellationToken);
            else
            {
                await playerClient.SwipeByPercentAsync(deviceName,
                    gesture.StartX / (double)screenWidth, gesture.StartY / (double)screenHeight,
                    gesture.EndX / (double)screenWidth, gesture.EndY / (double)screenHeight,
                    swipeDurationMs, cancellationToken);
            }
            // The automation loop owns the post-swipe settle interval because it
            // selects it from the current recognition mode.  Waiting here as well
            // added a second fixed delay before every board verification.
            if (settleDelayMs > 0)
                await Task.Delay(settleDelayMs, cancellationToken);
            return new Fruit2048SwipeExecution
            {
                StartX = gesture.StartX, StartY = gesture.StartY,
                EndX = gesture.EndX, EndY = gesture.EndY,
                DurationMs = swipeDurationMs, CommandAccepted = true
            };
        }

        private static GesturePoints ResolveGesture(Fruit2048Move move, ImageRegion boardRegion)
        {
            ImageRegion middleCell = Fruit2048ScreenProfile.GetRecognitionCellRegion(boardRegion, 1, 1);
            int startX = middleCell.X + (middleCell.Width / 2);
            int startY = middleCell.Y + (middleCell.Height / 2);
            int horizontalTravel = Math.Max(1, (int)Math.Round(boardRegion.Width * .40));
            int verticalTravel = Math.Max(1, (int)Math.Round(boardRegion.Height * .40));
            int endX = startX;
            int endY = startY;
            switch (move)
            {
                case Fruit2048Move.Left: endX -= horizontalTravel; break;
                case Fruit2048Move.Right: endX += horizontalTravel; break;
                case Fruit2048Move.Up: endY -= verticalTravel; break;
                case Fruit2048Move.Down: endY += verticalTravel; break;
                default: throw new ArgumentOutOfRangeException(nameof(move));
            }

            const int inset = 6;
            endX = Math.Max(boardRegion.X + inset, Math.Min(boardRegion.X + boardRegion.Width - inset, endX));
            endY = Math.Max(boardRegion.Y + inset, Math.Min(boardRegion.Y + boardRegion.Height - inset, endY));
            return new GesturePoints(startX, startY, endX, endY);
        }

        private sealed class GesturePoints
        {
            public GesturePoints(int startX, int startY, int endX, int endY)
            {
                StartX = startX; StartY = startY; EndX = endX; EndY = endY;
            }
            public int StartX { get; }
            public int StartY { get; }
            public int EndX { get; }
            public int EndY { get; }
        }

    }
}
