using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine.Physics;

using SDL3;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises actual SDL event routing with window-to-pixel density conversion.</summary>
    public sealed class SdlRopeInputTests
    {
        /// <summary>Primary mouse motion cuts through the same fitted canvas at high pixel density.</summary>
        [Fact]
        public void RoutesPrimaryMouseAndCancelsOnFocusLoss()
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            RopeInput input = new(scene, new(640, 960));
            SdlRopeInput router = new(input);
            SDL.Event down = new()
            {
                Button = new()
                {
                    Type = SDL.EventType.MouseButtonDown,
                    Button = 1,
                    X = 350,
                    Y = 138
                }
            };
            SDL.Event move = new()
            {
                Motion = new()
                {
                    Type = SDL.EventType.MouseMotion,
                    X = 450,
                    Y = 138
                }
            };
            router.Handle(down, 640, 960, 1280, 1920);
            router.Handle(move, 640, 960, 1280, 1920);
            router.Handle(new()
            {
                Type = (uint)SDL.EventType.WindowFocusLost
            }, 640, 960, 1280, 1920);
            input.ApplyPending();
            Assert.Empty(input.Cuts);
            router.Handle(down, 640, 960, 1280, 1920);
            router.Handle(move, 640, 960, 1280, 1920);
            router.Handle(new()
            {
                Button = new()
                {
                    Type = SDL.EventType.MouseButtonUp,
                    Button = 1,
                    X = 450,
                    Y = 138
                }
            }, 640, 960, 1280, 1920);
            input.ApplyPending();
            _ = Assert.Single(input.Cuts);
        }
    }
}
