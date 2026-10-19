using System;
using System.Linq;

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
            if (CharacterState == CharacterState.Idle && motion.IsFalling)
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
