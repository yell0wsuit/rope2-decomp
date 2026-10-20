using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

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
        StandUpChewing,
        /// <summary>Sliding along a surface parallel to the body until the slide scene ends.</summary>
        Sliding,
        /// <summary>Puzzled after the mouth closed without candy, until that scene ends.</summary>
        Puzzled,
        /// <summary>Turning to face left until the turn scene ends.</summary>
        TurnLeft,
        /// <summary>Turning to face right until the turn scene ends.</summary>
        TurnRight,
        /// <summary>Held by a thrower until thrown.</summary>
        ThrowerCaptured,
        /// <summary>Carried by a helicopter.</summary>
        HelicopterIdle,
        /// <summary>Carried by a helicopter with candy nearby.</summary>
        HelicopterMouthOpen,
        /// <summary>Carried by a helicopter while chewing.</summary>
        HelicopterChewing,
        /// <summary>Carried by a helicopter after the mouth closed without candy.</summary>
        HelicopterPuzzled,
        /// <summary>Cheering after the candy collected a star, until that scene ends.</summary>
        Excited,
        /// <summary>Cheering while carried by a helicopter, until that scene ends.</summary>
        HelicopterExcited,
        /// <summary>Scared by a Booka, until the WITH_BOOKA scene ends and the character jumps away.</summary>
        Booed
    }

    /// <summary>One accepted pre-step candy/character contact.</summary>
    /// <param name="Tick">Zero-based fixed contact tick.</param>
    /// <param name="Candy">Authored candy index.</param>
    /// <param name="Character">Authored character index.</param>
    public sealed record CandyConsumption(int Tick, int Candy, int Character);

    /// <summary>Opt-in gameplay state for one candy and character in a dry level of any location.</summary>
    public sealed partial class LevelGameplay
    {
        private readonly CandySimulation simulation;
        private readonly Vector2 canvas;
        private readonly List<CandyConsumption> consumptions = [];
        private float eatingTime;
        private float chewingTime;
        private readonly IReadOnlyDictionary<string, double>? sceneDurations;
        private int completedRevision = -1;
        private float lastSlideAngle = 3.4028e38f;
        private int excitedTimes;
        private Vector2 booPosition;

        internal LevelGameplay(CandySimulation simulation, LevelLayout layout, bool firstLevelArrival, IReadOnlyDictionary<string, double>? sceneDurations = null)
        {
            if (simulation.Candies.Count != 1 || simulation.Characters.Count != 1 ||
                layout.Objects.Any(o => o.Source.Kind is not ("candy" or "omnom" or "star" or "grab" or "polygon_materialized" or "rectangle_materialized" or "circle_materialized" or "pin" or "trigger" or "frog" or "balloon" or "rope_instance" or "pump" or "thrower" or "helicopter" or "fireball" or "booka" or "tutorial_swipe" or "tutorial_tap")) ||
                (!firstLevelArrival && layout.Objects.Any(o => o.Source.Kind == "omnom" && o.Position!.Value.Y < 0)))
            {
                throw new InvalidDataException("Play supports one candy and one Om Nom in a dry level; negative spawn requires the first production level and other mechanics are pending.");
            }
            this.simulation = simulation;
            this.sceneDurations = sceneDurations;
            canvas = layout.Size;
            // The source faces right when the character starts left of the screen center.
            FacingRight = (float)(simulation.Characters[0].Origin.X * 200.0) < (float)(canvas.X / 2.0);
            ArtFacingRight = FacingRight;
            Consumptions = consumptions.AsReadOnly();
            ArrivalLeaves = firstLevelArrival ? new ForestLeafBurst((simulation.Characters[0].Origin * 200) + new Vector2(0, 20)) : null;
            PlacedLevelObject[] hints = [.. layout.Objects.Where(o => o.Source.Kind is "tutorial_swipe" or "tutorial_tap")];
            if (hints.Length > 1)
            {
                throw new InvalidDataException("Multiple swipe hints are outside the supported gameplay slice.");
            }
            // The scene builds tutorial fingers in every mode but only shows them outside the challenges.
            if (hints.Length == 1 && simulation.Variant == LevelVariant.Main)
            {
                float delay = NativeText.FloatValue(hints[0].Source.Attributes.GetValueOrDefault("delay"));
                if (delay is < 0 or > 36000)
                {
                    throw new InvalidDataException("Invalid swipe hint delay.");
                }
                Tutorial = new(hints[0].Position!.Value, hints[0].Angle, delay, hints[0].Source.Kind == "tutorial_swipe");
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
        /// <summary>Gets whether the character faces right; the source flips its artwork when it does.</summary>
        public bool FacingRight
        {
            get; private set;
        }
        /// <summary>Gets whether the character artwork is mirrored to face right.</summary>
        /// <value>The source mirrors the artwork whenever the character faces right, but only a finished turn (or a
        /// procedural scale timeline, which rewrites the scale every frame) mirrors it back, so the artwork can keep
        /// facing right after the character turns its attention left.</value>
        public bool ArtFacingRight
        {
            get; private set;
        }
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
        /// <summary>Gets the optional nonphysical tap or swipe hint and its fixed-tick timing.</summary>
        public TutorialFinger? Tutorial
        {
            get;
        }
        /// <summary>Notifies the authored hint of a primary pointer press.</summary>
        public void NotifyTouchDown()
        {
            Tutorial?.TouchDown();
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
                // A thrower holding the candy swaps it for the character; a helicopter carrying it lets go.
                simulation.SwapThrowerCapture(candy, character);
                simulation.DropFromHelicopter(candy);
                CandyState = CandyState.Eating;
                bool airborne = CharacterState == CharacterState.FallingMouthOpen, carried = CharacterState == CharacterState.HelicopterMouthOpen;
                CharacterState = carried ? CharacterState.HelicopterChewing : airborne ? CharacterState.FallingChewing : CharacterState.Chewing;
                candy.IsSensor = true;
                candy.GravityScale = 0;
                candy.Velocity = 5 * (character.Origin - candy.Position);
                SetScene(carried ? "WITH_HELICOPTER_MOUTH_CLOSE" : airborne ? "FALLING_MOUTH_CLOSE" : "MOUTH_CLOSE");
            }
        }

        internal void AfterStep()
        {
            bool scaling = ProcedureActive;
            UpdateCharacter();
            // The source ends each character update by mirroring a right-facing character; a running scale timeline has
            // just rewritten the scale with a positive value, so the mirror then follows the facing direction exactly.
            if (scaling || ProcedureActive)
            {
                ArtFacingRight = FacingRight;
            }
            else if (FacingRight)
            {
                ArtFacingRight = true;
            }
        }

        private void UpdateCharacter()
        {
            if (Result != LevelResult.Running)
            {
                return;
            }
            ArrivalLeaves?.Step();
            Tutorial?.Step();
            PhysicsBody candy = simulation.Candies[0], character = simulation.Characters[0];
            ProcessImpacts();
            AdvanceProcedure();
            CompleteAuthoredScene();
            motion.Sample(character.Velocity, character.AngularVelocity);
            BooTimer -= PhysicsWorld.TimeStep;
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
                UpdateTurn();
                float distance = Vector2.Distance(character.Position, candy.Position) * 200;
                if (CharacterState is CharacterState.Idle or CharacterState.Falling && distance < 150)
                {
                    FacingRight = (float)(candy.Position.X * 200.0) > CharacterX();
                }
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
                else if (CharacterState == CharacterState.HelicopterIdle && distance < 150)
                {
                    FacingRight = (float)(candy.Position.X * 200.0) > CharacterX();
                    CharacterState = CharacterState.HelicopterMouthOpen;
                    SetScene("WITH_HELICOPTER_MOUTH_OPEN");
                }
                else if (CharacterState == CharacterState.HelicopterMouthOpen && distance > 170)
                {
                    CharacterState = CharacterState.HelicopterIdle;
                    SetScene("WITH_HELICOPTER_MOUTH_CLOSE");
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
            // The source finishes the Booka scenes by index before any state check.
            if (scene == "WITH_BOOKA")
            {
                JumpOfBoo();
            }
            else if (scene == "WITH_BOOKA_JUMP")
            {
                SetScene("FALLING");
            }
            else if (CharacterState == CharacterState.Fail)
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
            else if (scene is "EXCITED" or "EXCITED_MORE" or "EXCITED_MOST")
            {
                CharacterState = CharacterState.Idle;
                SetScene("IDLE");
            }
            else if (scene is "WITH_HELICOPTER_TAKE" or "WITH_HELICOPTER_PUZZLED" or "WITH_HELICOPTER_EXCITED")
            {
                CharacterState = CharacterState.HelicopterIdle;
                SetScene("WITH_HELICOPTER_IDLE");
            }
            else if (scene == "WITH_HELICOPTER_MOUTH_CLOSE")
            {
                if (CharacterState == CharacterState.HelicopterChewing)
                {
                    SetScene("WITH_HELICOPTER_CHEWING");
                }
                else
                {
                    CharacterState = CharacterState.HelicopterPuzzled;
                    SetScene("WITH_HELICOPTER_PUZZLED");
                }
            }
            else if (scene == "WITH_HELICOPTER_CHEWING")
            {
                Finish(LevelResult.Won);
            }
            else if (CharacterState == CharacterState.ThrowerCaptured)
            {
                // A held character returns to the idle animation without leaving the thrower.
                SetScene("IDLE");
            }
            else if (CharacterState == CharacterState.Sliding)
            {
                // Any finished timeline ends a slide; the source plays no new scene.
                CharacterState = CharacterState.Idle;
            }
            else if (CharacterState is CharacterState.TurnLeft or CharacterState.TurnRight && scene == "TURN")
            {
                FacingRight = ArtFacingRight = CharacterState == CharacterState.TurnRight;
                CharacterState = CharacterState.Idle;
                SetScene("IDLE");
            }
            else if (scene == "MOUTH_CLOSE")
            {
                float x = CharacterX(), center = (float)(canvas.X / 2.0);
                if (x < center + -50)
                {
                    FacingRight = true;
                }
                else if (x > center + 50)
                {
                    FacingRight = false;
                }
                CharacterState = CharacterState.Puzzled;
                SetScene("PUZZLED");
            }
            else if (scene == "PUZZLED")
            {
                CharacterState = CharacterState.Idle;
                SetScene("IDLE");
            }
        }

        /// <summary>Completes the current authored scene on the first fixed tick at or beyond its duration, once per revision.</summary>
        private void CompleteAuthoredScene()
        {
            if (sceneDurations != null && completedRevision != SceneRevision && !ProcedureActive &&
                sceneDurations.TryGetValue(Scene, out double duration) && (simulation.Tick - SceneStartedAt) * (double)PhysicsWorld.TimeStep >= duration)
            {
                completedRevision = SceneRevision;
                SceneFinished(Scene, SceneRevision);
            }
        }

        /// <summary>Native <c>Omnom::capturedByThrower</c>: a character that is neither chewing nor failed shows the thrower hold.</summary>
        internal void CharacterCaptured()
        {
            if (!Chewing && CharacterState != CharacterState.Fail)
            {
                CharacterState = CharacterState.ThrowerCaptured;
                SetScene("WITH_THROWER");
            }
        }

        /// <summary>Native <c>Omnom::releasedByThrower</c>: a thrown character falls, chewing if it was.</summary>
        internal void CharacterReleased()
        {
            if (Chewing)
            {
                CharacterState = CharacterState.FallingChewing;
                SetScene("FALLING_CHEWING");
            }
            else if (CharacterState != CharacterState.Fail)
            {
                CharacterState = CharacterState.Falling;
                SetScene("FALLING");
            }
            collidedWithTerrain = false;
        }

        /// <summary>Gets the Booka cooldown: Om Nom ignores Bookas while it is positive.</summary>
        internal float BooTimer
        {
            get; private set;
        }

        /// <summary>Native <c>Omnom::scheduleBooFromPosition</c>: a chewing character jumps away from the Booka at once; any
        /// other character except a failed one shows WITH_BOOKA and ignores Bookas for 0.15 seconds.</summary>
        /// <param name="booka">The Booka's center in meters.</param>
        /// <remarks>The iOS build keeps a thrower-held character in its thrower state instead; this follows the Android source.</remarks>
        internal void BooFrom(Vector2 booka)
        {
            booPosition = booka;
            if (Chewing)
            {
                JumpOfBoo();
            }
            else if (CharacterState != CharacterState.Fail)
            {
                BooTimer = .15f;
                CharacterState = CharacterState.Booed;
                SetScene("WITH_BOOKA");
            }
        }

        /// <summary>Native <c>Omnom::jumpOfBoo</c>: the character falls (chewing if it was), faces away from the Booka and is
        /// stopped and kicked by <c>boo_jump_power * 0.3</c>, tilted <c>90 - boo_jump_angle</c> degrees from straight up away
        /// from it, using the game's float rotation.</summary>
        private void JumpOfBoo()
        {
            if (Chewing)
            {
                CharacterState = CharacterState.FallingChewing;
                SetScene("FALLING_CHEWING");
            }
            else
            {
                CharacterState = CharacterState.Falling;
                SetScene("WITH_BOOKA_JUMP");
            }
            collidedWithTerrain = false;
            float editorAngle = NativeText.FloatValue(simulation.CharacterDesign.GetValueOrDefault("boo_jump_angle"));
            float editorPower = NativeText.FloatValue(simulation.CharacterDesign.GetValueOrDefault("boo_jump_power"));
            float add = (float)((float)(90.0 - editorAngle) * 3.14159265 / 180.0), angle = 0;
            Vector2 up = NativeVector.Rotate(new(0, -1), angle);
            PhysicsBody character = simulation.Characters[0];
            Vector2 away = booPosition - character.Position;
            if ((up.X * away.Y) - (up.Y * away.X) >= 0)
            {
                add = -add;
                FacingRight = ArtFacingRight = true;
            }
            else
            {
                FacingRight = ArtFacingRight = false;
            }
            Vector2 impulse = NativeVector.Rotate(new(0, (float)(editorPower * -0.3)), angle + add);
            character.SetLinearVelocity(Vector2.Zero);
            character.ApplyLinearImpulse(impulse, character.Position);
        }

        /// <summary>Native <c>Omnom::excite</c> for a star the candy collected: an idle character cheers, more with each star,
        /// and a carried one cheers in the helicopter; any other state only counts the star.</summary>
        internal void Excite()
        {
            excitedTimes++;
            if (CharacterState == CharacterState.Idle)
            {
                CharacterState = CharacterState.Excited;
                SetScene(excitedTimes == 1 ? "EXCITED" : excitedTimes == 2 ? "EXCITED_MORE" : "EXCITED_MOST");
            }
            else if (CharacterState == CharacterState.HelicopterIdle)
            {
                CharacterState = CharacterState.HelicopterExcited;
                SetScene("WITH_HELICOPTER_EXCITED");
            }
        }

        /// <summary>Native <c>Omnom::capturedByHelicopter</c>.</summary>
        internal void CharacterHelicopterCaptured()
        {
            if (Chewing)
            {
                CharacterState = CharacterState.HelicopterChewing;
                SetScene("WITH_HELICOPTER_CHEWING");
            }
            else if (CharacterState == CharacterState.Fail)
            {
                SetScene("WITH_HELICOPTER_FAIL");
            }
            else
            {
                CharacterState = CharacterState.HelicopterIdle;
                SetScene("WITH_HELICOPTER_TAKE");
            }
        }

        /// <summary>Native <c>Omnom::releasedByHelicopter</c>: a dropped character falls, chewing if it was.</summary>
        internal void CharacterHelicopterReleased()
        {
            if (Chewing)
            {
                CharacterState = CharacterState.FallingChewing;
                SetScene("FALLING_CHEWING");
            }
            else if (CharacterState == CharacterState.Fail)
            {
                SetScene("FAIL");
            }
            else
            {
                CharacterState = CharacterState.Falling;
                SetScene("FALLING");
            }
            collidedWithTerrain = false;
        }

        /// <summary>Gets whether the character is in a source chewing state.</summary>
        private bool Chewing => CharacterState is CharacterState.Chewing or CharacterState.FallingChewing or CharacterState.StandUpChewing or CharacterState.HelicopterChewing;

        /// <summary>Gets the character's logical x like the source node, from the body origin.</summary>
        private float CharacterX()
        {
            return (float)(simulation.Characters[0].Origin.X * 200.0);
        }

        /// <summary>Native idle turn: an upright, settled character away from the center turns to face it.</summary>
        private void UpdateTurn()
        {
            PhysicsBody body = simulation.Characters[0];
            if (CharacterState != CharacterState.Idle || Scene != "IDLE" || motion.Quarter.Length() >= .2f || MathF.Abs(motion.QuarterAngular) >= .1f ||
                Math.Abs(BodyDegrees(body)) >= 20)
            {
                return;
            }
            float x = CharacterX(), center = (float)(canvas.X / 2.0);
            if (!FacingRight && x < center + -50)
            {
                CharacterState = CharacterState.TurnRight;
                SetScene("TURN");
            }
            else if (FacingRight && x > center + 50)
            {
                CharacterState = CharacterState.TurnLeft;
                SetScene("TURN");
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
        /// <summary>Destroys the single gameplay candy like the native body teardown: every rope holding it at either end is
        /// first cut next to it, which also sends attached balloons flying.</summary>
        internal void RetireCandy()
        {
            PhysicsBody candy = Candies[0];
            ForgetThrownBody(candy);
            for (int r = 0; r < ropes.Count; r++)
            {
                CutWithBodyDestroyed(r, candy);
            }
            _ = World.RemoveBody(candy);
        }
    }
}
