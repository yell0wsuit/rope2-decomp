using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Result of a supported gameplay session.</summary>
    public enum LevelResult
    {
        /// <summary>The level is still active.</summary>
        Running,
        /// <summary>Om Nom has finished chewing.</summary>
        Won,
        /// <summary>The candy or character left the supported dry scene.</summary>
        Lost
    }

    /// <summary>Lifecycle of the single supported candy.</summary>
    public enum CandyState
    {
        /// <summary>Can collect stars and be eaten.</summary>
        Active,
        /// <summary>Fading sensor moving toward Om Nom.</summary>
        Eating,
        /// <summary>No longer belongs to the physics world.</summary>
        Removed
    }

    /// <summary>Supported character presentation states.</summary>
    public enum CharacterState
    {
        /// <summary>Ordinary idle pose.</summary>
        Idle,
        /// <summary>Candy is within the opening distance.</summary>
        MouthOpen,
        /// <summary>Sustained downward or upward airborne motion.</summary>
        Falling,
        /// <summary>Airborne character with candy nearby.</summary>
        FallingMouthOpen,
        /// <summary>Closing the airborne mouth after candy moves away.</summary>
        FallingMouthClose,
        /// <summary>Chewing while airborne until a supported landing or completion.</summary>
        FallingChewing,
        /// <summary>Eating and chewing after physical contact.</summary>
        Chewing,
        /// <summary>A required object left the scene.</summary>
        Fail,
        /// <summary>Procedural root deformation after a terrain impact.</summary>
        Bumping,
        /// <summary>Rolling after an impact until angular history settles.</summary>
        Rolling,
        /// <summary>Preparing or playing the authored stand-up response.</summary>
        StandUp,
        /// <summary>Standing while chewing, with the original fallback timer retained.</summary>
        StandUpChewing
    }

    /// <summary>One accepted pre-step candy/character contact.</summary>
    /// <param name="Tick">Zero-based fixed contact tick.</param>
    /// <param name="Candy">Authored candy index.</param>
    /// <param name="Character">Authored character index.</param>
    public sealed record CandyConsumption(int Tick, int Candy, int Character);

    /// <summary>Opt-in gameplay state for one ordinary dry forest candy and character.</summary>
    public sealed partial class LevelGameplay
    {
        private readonly CandySimulation simulation;
        private readonly Vector2 canvas;
        private readonly List<CandyConsumption> consumptions = [];
        private float eatingTime;
        private float chewingTime;

        internal LevelGameplay(CandySimulation simulation, LevelLayout layout, LevelDefinition level, bool firstLevelArrival)
        {
            if (simulation.Candies.Count != 1 || simulation.Characters.Count != 1 ||
                layout.Objects.Any(o => o.Source.Kind is not ("candy" or "omnom" or "star" or "grab" or "polygon_materialized" or "tutorial_swipe")) ||
                (!firstLevelArrival && layout.Objects.Any(o => o.Source.Kind == "omnom" && o.Position!.Value.Y < 0)) ||
                level.Settings.Any(s => s.Kind == "gameDesign" && s.Attributes.GetValueOrDefault("location", "forest") != "forest"))
            {
                throw new InvalidDataException("Play supports one candy and one Om Nom in a dry forest level; negative spawn requires the first forest entry and other mechanics are pending.");
            }
            this.simulation = simulation;
            canvas = layout.Size;
            Consumptions = consumptions.AsReadOnly();
            ArrivalLeaves = firstLevelArrival ? new ForestLeafBurst((simulation.Characters[0].Origin * 200) + new Vector2(0, 20)) : null;
            PlacedLevelObject[] hints = [.. layout.Objects.Where(o => o.Source.Kind == "tutorial_swipe")];
            if (hints.Length > 1)
            {
                throw new InvalidDataException("Multiple swipe hints are outside the supported gameplay slice.");
            }
            if (hints.Length == 1)
            {
                if (!float.TryParse(hints[0].Source.Attributes.GetValueOrDefault("delay", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out float delay) || !float.IsFinite(delay) || delay < 0 || delay > 36000)
                {
                    throw new InvalidDataException("Invalid swipe hint delay.");
                }
                SwipeTutorial = new(hints[0].Position!.Value, hints[0].Angle, delay);
            }
        }

        /// <summary>Gets the current level result.</summary>
        public LevelResult Result
        {
            get; private set;
        }
        /// <summary>Gets the candy lifecycle phase.</summary>
        public CandyState CandyState
        {
            get; private set;
        }
        /// <summary>Gets the supported character pose state.</summary>
        public CharacterState CharacterState
        {
            get; private set;
        }
        /// <summary>Gets the completed fixed-tick count at the terminal result.</summary>
        public int? FinishedAt
        {
            get; private set;
        }
        /// <summary>Gets accepted one-time contact events.</summary>
        public IReadOnlyList<CandyConsumption> Consumptions
        {
            get;
        }
        /// <summary>Gets the matching Windows animation scene for supported state playback.</summary>
        public string Scene { get; private set; } = "IDLE";
        /// <summary>Gets the scene revision, used to reject callbacks from replaced animations.</summary>
        public int SceneRevision
        {
            get; private set;
        }
        /// <summary>Gets the completed-tick count when this animation began.</summary>
        public int SceneStartedAt
        {
            get; private set;
        }
        /// <summary>Gets the opacity during the original short candy disappearance.</summary>
        public float CandyOpacity => CandyState == CandyState.Active ? 1 : CandyState == CandyState.Removed ? 0 : Math.Clamp(1 - (eatingTime / .2f), 0, 1);
        /// <summary>Gets the optional first-entry leaf burst; it never participates in physics.</summary>
        public ForestLeafBurst? ArrivalLeaves
        {
            get;
        }
        /// <summary>Gets the optional nonphysical swipe hint and its fixed-tick timing.</summary>
        public SwipeTutorial? SwipeTutorial
        {
            get;
        }
        /// <summary>Notifies the authored hint of a primary pointer press.</summary>
        public void NotifyTouchDown()
        {
            SwipeTutorial?.TouchDown();
        }

        internal void BeforeStep()
        {
            if (Result != LevelResult.Running || CandyState != CandyState.Active || CharacterState == CharacterState.Fail)
            {
                return;
            }
            PhysicsBody candy = simulation.Candies[0], character = simulation.Characters[0];
            if (character.PolygonFixtures.Any(f => f.OverlapsCircle(candy.Position, candy.Radius, character.Origin, character.Angle)))
            {
                consumptions.Add(new(simulation.Tick, 0, 0));
                CandyState = CandyState.Eating;
                bool airborne = CharacterState == CharacterState.FallingMouthOpen;
                CharacterState = airborne ? CharacterState.FallingChewing : CharacterState.Chewing;
                candy.IsSensor = true;
                candy.GravityScale = 0;
                candy.Velocity = 5 * (character.Origin - candy.Position);
                SetScene(airborne ? "FALLING_MOUTH_CLOSE" : "MOUTH_CLOSE");
            }
        }

        internal void AfterStep()
        {
            if (Result != LevelResult.Running)
            {
                return;
            }
            ArrivalLeaves?.Step();
            SwipeTutorial?.Step();
            PhysicsBody candy = simulation.Candies[0], character = simulation.Characters[0];
            ProcessImpacts();
            AdvanceProcedure();
            motion.Sample(character.Velocity, character.AngularVelocity);
            collidedWithTerrain |= simulation.CharacterTouchesTerrain();
            if (Outside(character.Origin, 70))
            {
                CharacterState = CharacterState.Fail;
                SetScene("FAIL");
                Finish(LevelResult.Lost);
                return;
            }
            if (CandyState == CandyState.Eating)
            {
                eatingTime += PhysicsWorld.TimeStep;
                if (eatingTime >= .2f)
                {
                    simulation.RetireCandy();
                    CandyState = CandyState.Removed;
                }
            }
            if (consumptions.Count > 0)
            {
                chewingTime += PhysicsWorld.TimeStep;
                if (chewingTime >= 2.8f)
                {
                    Finish(LevelResult.Won);
                }
                UpdateAirborneState();
                UpdateStandingState();
                return;
            }
            if (CharacterState == CharacterState.Fail)
            {
                return;
            }
            if (CandyState == CandyState.Active && Outside(candy.Origin, 40))
            {
                simulation.RetireCandy();
                CandyState = CandyState.Removed;
                CharacterState = CharacterState.Fail;
                SetScene("FAIL");
            }
            else
            {
                float distance = Vector2.Distance(character.Position, candy.Position) * 200;
                if (CharacterState == CharacterState.Idle && distance < 150)
                {
                    CharacterState = CharacterState.MouthOpen;
                    SetScene("MOUTH_OPEN");
                }
                else if (CharacterState == CharacterState.MouthOpen && distance > 170)
                {
                    CharacterState = CharacterState.Idle;
                    SetScene("MOUTH_CLOSE");
                }
                else if (CharacterState == CharacterState.Falling && distance < 150)
                {
                    CharacterState = CharacterState.FallingMouthOpen;
                    SetScene("FALLING_MOUTH_OPEN");
                }
                else if (CharacterState == CharacterState.FallingMouthOpen && distance > 170)
                {
                    CharacterState = CharacterState.FallingMouthClose;
                    SetScene("FALLING_MOUTH_CLOSE");
                }
                UpdateAirborneState();
                UpdateStandingState();
            }
        }

        /// <summary>Accepts completion only from the currently selected scene and revision.</summary>
        /// <param name="scene">Scene whose playback finished.</param>
        /// <param name="revision">Scene revision captured when playback began.</param>
        public void SceneFinished(string scene, int revision)
        {
            if (Result != LevelResult.Running || Scene != scene || SceneRevision != revision || ProcedureActive || CharacterState == CharacterState.StandUpChewing)
            {
                return;
            }
            if (CharacterState == CharacterState.Fail)
            {
                Finish(LevelResult.Lost);
            }
            else if (CharacterState == CharacterState.StandUp && scene == "STAND_UP")
            {
                CharacterState = CharacterState.Idle;
                SetScene("IDLE");
            }
            else if (CharacterState == CharacterState.FallingChewing)
            {
                if (scene == "FALLING_MOUTH_CLOSE")
                {
                    SetScene("FALLING_CHEWING");
                }
                else if (scene == "FALLING_CHEWING")
                {
                    Finish(LevelResult.Won);
                }
            }
            else if (CharacterState == CharacterState.FallingMouthClose && scene == "FALLING_MOUTH_CLOSE")
            {
                CharacterState = CharacterState.Falling;
                collidedWithTerrain = false;
                SetScene("FALLING");
            }
            else if (CharacterState == CharacterState.Chewing)
            {
                if (scene == "MOUTH_CLOSE")
                {
                    SetScene("CHEWING");
                }
                else if (scene == "CHEWING")
                {
                    Finish(LevelResult.Won);
                }
            }
            else if (scene == "MOUTH_CLOSE")
            {
                SetScene("PUZZLED");
            }
            else if (scene == "PUZZLED")
            {
                SetScene("IDLE");
            }
        }

        private void SetScene(string scene)
        {
            procedure = CharacterProcedure.None;
            Scene = scene;
            SceneRevision++;
            SceneStartedAt = simulation.Tick;
        }

        private void Finish(LevelResult result)
        {
            Result = result;
            FinishedAt = simulation.Tick;
        }

        private bool Outside(Vector2 meters, int margin)
        {
            Vector2 point = meters * 200;
            int expanded = margin + 150;
            return point.X < -expanded || point.Y < -expanded || point.X > canvas.X + expanded || point.Y > canvas.Y + expanded;
        }
    }

    public sealed partial class CandySimulation
    {
        /// <summary>Destroys the single gameplay candy and retires every terminal rope attachment.</summary>
        internal void RetireCandy()
        {
            PhysicsBody candy = Candies[0];
            _ = world.RemoveBody(candy);
            foreach (RopeChain rope in ropes.Where(r => r.Points[^1] == candy))
            {
                if (rope.Stretch != null)
                {
                    _ = world.Remove(rope.Stretch);
                    rope.Stretch = null;
                }
                rope.Released = true;
            }
        }
    }
}
