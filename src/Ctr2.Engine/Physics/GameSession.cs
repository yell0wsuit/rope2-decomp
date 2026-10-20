using System;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Owns one gameplay simulation and its pause/restart/input clock.</summary>
    public sealed class GameSession
    {
        private readonly LevelDefinition level;
        private readonly LevelDefinition design;
        private readonly bool firstLevelArrival;
        private readonly SceneTimings? timings;
        private readonly LevelVariant variant;
        private SimulationClock clock = new();
        private double hostTime;
        private double activeTime;
        private bool rebase;
        private bool paused;
        /// <summary>Creates a supported session from immutable authored level and design definitions.</summary>
        /// <param name="level">Original editor-space level.</param>
        /// <param name="design">Matching master design document.</param>
        /// <param name="firstLevelArrival">Whether the host selected the first production level.</param>
        /// <param name="timings">Optional animation durations; see <see cref="CandySimulation"/>.</param>
        /// <param name="variant">Game mode to build the scene for.</param>
        public GameSession(LevelDefinition level, LevelDefinition design, bool firstLevelArrival = false, SceneTimings? timings = null,
            LevelVariant variant = LevelVariant.Main)
        {
            this.variant = variant;
            this.level = level;
            this.design = design;
            this.firstLevelArrival = firstLevelArrival;
            this.timings = timings;
            Simulation = new(level, design, play: true, firstLevelArrival: firstLevelArrival, timings, variant);
            Input = new(Simulation, LevelLayout.Create(level).Size);
        }
        /// <summary>Gets the current simulation, replaced on restart.</summary>
        public CandySimulation Simulation
        {
            get; private set;
        }
        /// <summary>Gets current pointer input ownership, replaced on restart.</summary>
        public RopeInput Input
        {
            get; private set;
        }
        /// <summary>Gets or sets whether simulation ticks are paused.</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                if (paused == value)
                {
                    return;
                }
                paused = value;
                rebase = true;
                Input.Cancel();
            }
        }
        /// <summary>Raised after each fixed step so playback callbacks use simulation time.</summary>
        public event Action? Stepped;
        /// <summary>Raised before each active fixed step for deterministic scheduled input.</summary>
        public event Action? Stepping;
        /// <summary>Restores the initial level and cancels held and queued input.</summary>
        public void Restart()
        {
            CandySimulation replacement = new(level, design, play: true, firstLevelArrival: firstLevelArrival, timings, variant);
            Input.Cancel();
            Simulation = replacement;
            Input = new(Simulation, LevelLayout.Create(level).Size);
            paused = false;
            rebase = true;
            activeTime = 0;
            clock = new();
        }
        /// <summary>Advances one tick when active.</summary>
        public void Step()
        {
            if (Paused || Simulation.Gameplay!.Result != LevelResult.Running)
            {
                Input.Cancel();
                return;
            }
            Stepping?.Invoke();
            Input.ApplyPending();
            Simulation.Step();
            Stepped?.Invoke();
            if (Simulation.Gameplay.Result != LevelResult.Running)
            {
                Input.Cancel();
            }
        }
        /// <summary>Advances toward monotonic host elapsed time, excluding paused intervals.</summary>
        public void AdvanceTo(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < hostTime || seconds > int.MaxValue * (double)PhysicsWorld.TimeStep)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            double delta = seconds - hostTime;
            hostTime = seconds;
            if (rebase)
            {
                rebase = false;
                return;
            }
            if (Paused || Simulation.Gameplay!.Result != LevelResult.Running)
            {
                return;
            }
            activeTime += delta;
            _ = clock.AdvanceTo(activeTime, Step);
        }
    }
}
