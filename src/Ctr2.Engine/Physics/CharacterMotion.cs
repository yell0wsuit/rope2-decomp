using System;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class LevelGameplay
    {
        private readonly CharacterMotionHistory motion = new();
        private bool collidedWithTerrain;

        private void UpdateAirborneState()
        {
            if (Result != LevelResult.Running)
            {
                return;
            }
            if (CharacterState == CharacterState.Rolling && MathF.Abs(motion.ShortAngular) < .1f && MathF.Abs(motion.LongAngular) < .1f)
            {
                CharacterState = CharacterState.Idle;
                SetScene("IDLE");
            }
            UpdateSliding();
            if (CharacterState is CharacterState.Idle or CharacterState.Excited && motion.IsFalling)
            {
                CharacterState = CharacterState.Falling;
                collidedWithTerrain = false;
                SetScene("FALLING");
            }
            if (CharacterState == CharacterState.Falling && collidedWithTerrain &&
                MathF.Abs(motion.Short.Y) < .2f && MathF.Abs(motion.Medium.Y) < .2f)
            {
                CharacterState = CharacterState.Idle;
                SetScene("IDLE");
            }
            if (CharacterState == CharacterState.FallingChewing && collidedWithTerrain &&
                MathF.Abs(motion.Short.Y) < .01f && MathF.Abs(motion.Long.Y) < .1f)
            {
                CharacterState = CharacterState.Chewing;
                SetScene("CHEWING");
            }
        }
    }

    public sealed partial class LevelGameplay
    {
        /// <summary>Native idle slide: recent motion over 0.5 m/s, heading 10 to 170 degrees, within one degree of the body
        /// angle (modulo 180) and more than 15 degrees from the previous slide's heading.</summary>
        private void UpdateSliding()
        {
            if (CharacterState != CharacterState.Idle)
            {
                return;
            }
            Vector2 average = motion.Short;
            float heading = (float)((float)(MathF.Atan2(average.Y, average.X) * 180.0) / 3.14159265);
            float body = (float)((float)(simulation.Characters[0].Angle * 180.0) / 3.14159265);
            float diff = heading - body;
            while (diff > 170)
            {
                diff = (float)(diff + -180.0);
            }
            while (diff < -170)
            {
                diff = (float)(diff + 180.0);
            }
            if (MathF.Abs(heading - lastSlideAngle) > 15 && heading > 10 && heading < 170 && average.Length() > .5f && MathF.Abs(diff) < 1)
            {
                CharacterState = CharacterState.Sliding;
                lastSlideAngle = heading;
                // The source plays SLIDE_LEFT when moving the way the character faces, otherwise SLIDE_RIGHT.
                SetScene((FacingRight ? average.X > 0 : average.X < 0) ? "SLIDE_LEFT" : "SLIDE_RIGHT");
            }
        }
    }

    public sealed partial class CandySimulation
    {
        internal bool CharacterTouchesTerrain()
        {
            return World.TouchesTerrain(Characters[0]);
        }
    }

    public sealed partial class PhysicsWorld
    {
        internal bool TouchesTerrain(PhysicsBody body)
        {
            return contacts.Values.Any(c => c.Body == body) ||
                bodyContacts.Values.Any(c => (c.A.Body == body && c.B.Body!.Land) || (c.B.Body == body && c.A.Body!.Land));
        }
    }
}
