using System.Numerics;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Tarkov.Features.Ballistics
{
    public static class BallisticsSimulation
    {
        // Maximum simulation iterations (*SHOULD* be enough for every round in the game)
        private const int _maxIterations = 1300;
        // The time step between each simulation iteration
        private const float _simTimeStep = 0.01f;
        // The target distance tolerance from the lerped Z component.
        private const float _optimalLerpTolerance = 0.001f;
        // Fake Unity Physics.gravity
        private static readonly Vector3 _gravity = new Vector3(0, -9.81f, 0);
        // Fake Unity Vector3.forward
        private static readonly Vector3 _forwardVector = new Vector3(0f, 0f, 1f);

        public static BallisticSimulationOutput Run(ref Vector3 startPosition, ref Vector3 endPosition, BallisticsInfo ballistics)
        {
            float shotDistance = Vector3.Distance(startPosition, endPosition);

            // Perform calculations based on the bullet properties
            float uE002 = ballistics.BulletMassGrams / 1000f;
            float uE004 = uE002 * 2f;
            float uE003 = ballistics.BulletDiameterMillimeters / 1000f;
            float uE005 = uE002 * 0.0014223f / (uE003 * uE003 * ballistics.BallisticCoefficient);
            float uE006 = uE003 * uE003 * 3.1415927f / 4f;
            float uE007 = 1.2f * uE006;

            // Working vars
            float time = 0f;
            float lastTravelTime = 0f;
            Vector3 lastPosition = new Vector3(0, 0, 0);
            Vector3 lastVelocity = _forwardVector * (float)ballistics.BulletSpeed;

            // Output vars
            float bulletDrop = 0f;
            float travelTime = 0f;

            for (int i = 1; i < _maxIterations; i++)
            {
                float magnitude = lastVelocity.Length();
                float dragCoefficient = G1.CalculateDragCoefficient(magnitude) * uE005;
                Vector3 translationOffset = _gravity + uE007 * -dragCoefficient * magnitude * magnitude / uE004 * Vector3.Normalize(lastVelocity);

                Vector3 currentPosition = lastPosition + lastVelocity * _simTimeStep + 5E-05f * translationOffset;
                Vector3 currentVelocity = lastVelocity + translationOffset * _simTimeStep;

                // Proceed with trajectory calculations until the distance from the target begins to increase.
                float currentDistance = Vector3.Distance(currentPosition, Vector3.Zero);
                if (currentDistance >= shotDistance)
                {
                    float optimalLerp = FindOptimalLerp(lastPosition, currentPosition, shotDistance);
                    // Get lerped values
                    bulletDrop = Math.Abs(Vector3.Lerp(lastPosition, currentPosition, optimalLerp).Y);
                    travelTime = float.Lerp(lastTravelTime, time += _simTimeStep, optimalLerp);

                    break;
                }
                else
                {
                    lastTravelTime = time += _simTimeStep;
                    lastPosition = currentPosition;
                    lastVelocity = currentVelocity;
                }
            }

            return new BallisticSimulationOutput(bulletDrop, travelTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FindOptimalLerp(Vector3 posBefore, Vector3 posAfter, float shotDistance)
        {
            float lerpMin = 0f;
            float lerpMax = 1f;
            float lerpMid = 0.5f;

            while (lerpMax - lerpMin > _optimalLerpTolerance)
            {
                Vector3 lerped = Vector3.Lerp(posBefore, posAfter, lerpMid);

                if (lerped.Z < shotDistance)
                    lerpMin = lerpMid;
                else
                    lerpMax = lerpMid;

                lerpMid = (lerpMin + lerpMax) / 2f;
            }

            return lerpMid;
        }
    }
}