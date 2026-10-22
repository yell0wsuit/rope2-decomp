namespace Ctr2.Engine.Physics
{
    public sealed partial class LevelGameplay
    {
        private string? connectedWaterScene;
        private bool WaterState => CharacterState is CharacterState.WaterDive or CharacterState.WaterSwimming or
            CharacterState.WaterMouthOpen or CharacterState.WaterMouthClose or CharacterState.WaterChewing;

        private void UpdateWaterMouth(float distance)
        {
            if (CharacterState == CharacterState.WaterSwimming && distance < 150)
            {
                FacingRight = (float)(simulation.Candies[0].Position.X * 200.0) > CharacterX();
                CharacterState = CharacterState.WaterMouthOpen;
                SetScene("WATER_MOUTH_OPEN");
                ArtFacingRight = FacingRight;
            }
            else if (CharacterState == CharacterState.WaterMouthOpen && distance > 170)
            {
                CharacterState = CharacterState.WaterMouthClose;
                SetScene("WATER_MOUTH_CLOSE");
                ArtFacingRight = FacingRight;
            }
        }

        /// <summary>Windows 0x4989F0 evaluates water state changes after the ordinary airborne and standing states.</summary>
        private void UpdateWaterState()
        {
            PhysicsBody character = simulation.Characters[0];
            if (CharacterState is CharacterState.Idle or CharacterState.Falling or CharacterState.Rolling &&
                simulation.UnderwaterAverage(character, 5) > .5f && BooTimer < 0)
            {
                CharacterState = CharacterState.WaterDive;
                StartWaterConnection("WATER_DIVE");
            }
            if (CharacterState == CharacterState.WaterSwimming && simulation.UnderwaterAverage(character, 59) < .4f)
            {
                CharacterState = CharacterState.Idle;
                StartWaterConnection("IDLE");
            }
        }

        /// <summary>Native connection-out squashes the previous scene for 0.125 seconds before selecting the target.</summary>
        private void StartWaterConnection(string target)
        {
            connectedWaterScene = target;
            StartProcedure(CharacterProcedure.WaterConnectionOut, 0);
        }

        /// <summary>Windows finishTransitionIntoTimeline (0x498960) selects the target and plays connection recovery.</summary>
        private void RecoverWaterConnection(string target)
        {
            SetScene(target);
            procedure = CharacterProcedure.WaterConnectionIn;
            procedureStartedAt = simulation.Tick;
        }

        private bool WaterConnection => procedure is CharacterProcedure.WaterConnectionOut or CharacterProcedure.WaterConnectionIn;
        private bool SceneCompletionBlocked => ProcedureActive && procedure != CharacterProcedure.WaterConnectionIn;
    }
}
