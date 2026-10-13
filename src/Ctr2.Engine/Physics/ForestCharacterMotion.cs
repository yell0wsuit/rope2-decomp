using System;
using System.Linq;

namespace Ctr2.Engine.Physics
{
    public sealed partial class ForestGameplay
    {
        private readonly CharacterMotionHistory motion = new();
        private bool collidedWithTerrain;

        private void UpdateAirborneState()
        {
            if (Result != ForestResult.Running)
            {
                return;
            }
            if (CharacterState == ForestCharacterState.Idle && motion.IsFalling)
            {
                CharacterState = ForestCharacterState.Falling;
                collidedWithTerrain = false;
                SetScene("FALLING");
            }
            if (CharacterState == ForestCharacterState.Falling && collidedWithTerrain &&
                MathF.Abs(motion.Short.Y) < .2f && MathF.Abs(motion.Medium.Y) < .2f)
            {
                CharacterState = ForestCharacterState.Idle;
                SetScene("IDLE");
            }
            if (CharacterState == ForestCharacterState.FallingChewing && collidedWithTerrain &&
                MathF.Abs(motion.Short.Y) < .01f && MathF.Abs(motion.Long.Y) < .1f)
            {
                CharacterState = ForestCharacterState.Chewing;
                SetScene("CHEWING");
            }
        }
    }

    public sealed partial class CandySimulation
    {
        internal bool CharacterTouchesTerrain()
        {
            return world.TouchesTerrain(Characters[0]);
        }
    }

    public sealed partial class PhysicsWorld
    {
        internal bool TouchesTerrain(PhysicsBody body)
        {
            return contacts.Values.Any(c => c.Body == body);
        }
    }
}
