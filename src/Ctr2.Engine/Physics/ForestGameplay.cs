using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Result of the supported ordinary forest session.</summary>
    public enum ForestResult
    {
        /// <summary>The level is still active.</summary>
        Running,
        /// <summary>Om Nom has finished chewing.</summary>
        Won,
        /// <summary>The candy or character left the supported dry scene.</summary>
        Lost
    }

    /// <summary>Lifecycle of the single supported candy.</summary>
    public enum ForestCandyState
    {
        /// <summary>Can collect stars and be eaten.</summary>
        Active,
        /// <summary>Fading sensor moving toward Om Nom.</summary>
        Eating,
        /// <summary>No longer belongs to the physics world.</summary>
        Removed
    }

    /// <summary>Supported character presentation states.</summary>
    public enum ForestCharacterState
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
        Fail
    }

    /// <summary>One accepted pre-step candy/character contact.</summary>
    /// <param name="Tick">Zero-based fixed contact tick.</param>
    /// <param name="Candy">Authored candy index.</param>
    /// <param name="Character">Authored character index.</param>
    public sealed record CandyConsumption(int Tick, int Candy, int Character);

    /// <summary>Opt-in gameplay state for one ordinary dry forest candy and character.</summary>
    public sealed partial class ForestGameplay
    {
        private readonly CandySimulation simulation;
        private readonly Vector2 canvas;
        private readonly List<CandyConsumption> consumptions = [];
        private float eatingTime;
        private float chewingTime;

        internal ForestGameplay(CandySimulation simulation, LevelLayout layout, LevelDefinition level)
        {
            if (simulation.Candies.Count != 1 || simulation.Characters.Count != 1 ||
                layout.Objects.Any(o => o.Source.Kind is not ("candy" or "omnom" or "star" or "grab" or "polygon_materialized")) ||
                layout.Objects.Any(o => o.Source.Kind == "omnom" && o.Position!.Value.Y < 0) ||
                level.Settings.Any(s => s.Kind == "gameDesign" && s.Attributes.GetValueOrDefault("location", "forest") != "forest"))
            {
                throw new InvalidDataException("Play supports one candy and one Om Nom in an ordinary dry forest level; special arrival and other mechanics are pending.");
            }
            this.simulation = simulation;
            canvas = layout.Size;
            Consumptions = consumptions.AsReadOnly();
        }

        /// <summary>Gets the current level result.</summary>
        public ForestResult Result
        {
            get; private set;
        }
        /// <summary>Gets the candy lifecycle phase.</summary>
        public ForestCandyState CandyState
        {
            get; private set;
        }
        /// <summary>Gets the supported character pose state.</summary>
        public ForestCharacterState CharacterState
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
        public float CandyOpacity => CandyState == ForestCandyState.Active ? 1 : CandyState == ForestCandyState.Removed ? 0 : Math.Clamp(1 - (eatingTime / .2f), 0, 1);

        internal void BeforeStep()
        {
            if (Result != ForestResult.Running || CandyState != ForestCandyState.Active || CharacterState == ForestCharacterState.Fail)
            {
                return;
            }
            PhysicsBody candy = simulation.Candies[0], character = simulation.Characters[0];
            if (character.PolygonFixtures.Any(f => f.OverlapsCircle(candy.Position, candy.Radius, character.Origin, character.Angle)))
            {
                consumptions.Add(new(simulation.Tick, 0, 0));
                CandyState = ForestCandyState.Eating;
                bool airborne = CharacterState == ForestCharacterState.FallingMouthOpen;
                CharacterState = airborne ? ForestCharacterState.FallingChewing : ForestCharacterState.Chewing;
                candy.IsSensor = true;
                candy.GravityScale = 0;
                candy.Velocity = 5 * (character.Origin - candy.Position);
                SetScene(airborne ? "FALLING_MOUTH_CLOSE" : "MOUTH_CLOSE");
            }
        }

        internal void AfterStep()
        {
            if (Result != ForestResult.Running)
            {
                return;
            }
            PhysicsBody candy = simulation.Candies[0], character = simulation.Characters[0];
            motion.Sample(character.Velocity);
            collidedWithTerrain |= simulation.CharacterTouchesTerrain();
            if (Outside(character.Origin, 70))
            {
                CharacterState = ForestCharacterState.Fail;
                SetScene("FAIL");
                Finish(ForestResult.Lost);
                return;
            }
            if (CandyState == ForestCandyState.Eating)
            {
                eatingTime += PhysicsWorld.TimeStep;
                if (eatingTime >= .2f)
                {
                    simulation.RetireCandy();
                    CandyState = ForestCandyState.Removed;
                }
            }
            if (CharacterState is ForestCharacterState.Chewing or ForestCharacterState.FallingChewing)
            {
                chewingTime += PhysicsWorld.TimeStep;
                if (chewingTime >= 2.8f)
                {
                    Finish(ForestResult.Won);
                }
                UpdateAirborneState();
                return;
            }
            if (CharacterState == ForestCharacterState.Fail)
            {
                return;
            }
            if (CandyState == ForestCandyState.Active && Outside(candy.Origin, 40))
            {
                simulation.RetireCandy();
                CandyState = ForestCandyState.Removed;
                CharacterState = ForestCharacterState.Fail;
                SetScene("FAIL");
            }
            else
            {
                float distance = Vector2.Distance(character.Position, candy.Position) * 200;
                if (CharacterState == ForestCharacterState.Idle && distance < 150)
                {
                    CharacterState = ForestCharacterState.MouthOpen;
                    SetScene("MOUTH_OPEN");
                }
                else if (CharacterState == ForestCharacterState.MouthOpen && distance > 170)
                {
                    CharacterState = ForestCharacterState.Idle;
                    SetScene("MOUTH_CLOSE");
                }
                else if (CharacterState == ForestCharacterState.Falling && distance < 150)
                {
                    CharacterState = ForestCharacterState.FallingMouthOpen;
                    SetScene("FALLING_MOUTH_OPEN");
                }
                else if (CharacterState == ForestCharacterState.FallingMouthOpen && distance > 170)
                {
                    CharacterState = ForestCharacterState.FallingMouthClose;
                    SetScene("FALLING_MOUTH_CLOSE");
                }
                UpdateAirborneState();
            }
        }

        /// <summary>Accepts completion only from the currently selected scene and revision.</summary>
        /// <param name="scene">Scene whose playback finished.</param>
        /// <param name="revision">Scene revision captured when playback began.</param>
        public void SceneFinished(string scene, int revision)
        {
            if (Result != ForestResult.Running || Scene != scene || SceneRevision != revision)
            {
                return;
            }
            if (CharacterState == ForestCharacterState.Fail)
            {
                Finish(ForestResult.Lost);
            }
            else if (CharacterState == ForestCharacterState.FallingChewing)
            {
                if (scene == "FALLING_MOUTH_CLOSE")
                {
                    SetScene("FALLING_CHEWING");
                }
                else if (scene == "FALLING_CHEWING")
                {
                    Finish(ForestResult.Won);
                }
            }
            else if (CharacterState == ForestCharacterState.FallingMouthClose && scene == "FALLING_MOUTH_CLOSE")
            {
                CharacterState = ForestCharacterState.Falling;
                collidedWithTerrain = false;
                SetScene("FALLING");
            }
            else if (CharacterState == ForestCharacterState.Chewing)
            {
                if (scene == "MOUTH_CLOSE")
                {
                    SetScene("CHEWING");
                }
                else if (scene == "CHEWING")
                {
                    Finish(ForestResult.Won);
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
            Scene = scene;
            SceneRevision++;
            SceneStartedAt = simulation.Tick;
        }

        private void Finish(ForestResult result)
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
