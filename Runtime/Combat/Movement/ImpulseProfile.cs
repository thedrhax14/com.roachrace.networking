using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking.Combat.Movement
{
    [CreateAssetMenu(fileName = "ImpulseProfile", menuName = "RoachRace/Combat/Movement/Impulse (Ballistic) Profile")]
    public class ImpulseProfile : ProjectileMovementProfile
    {
        [Header("Ballistic Settings")]
        public bool useGravity = true;

        public override void OnInitialize(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier)
        {
            rb.useGravity = useGravity;
            
            // Apply initial velocity once
            Vector3 dir = t.TransformDirection(moveDirection.normalized);
            float spd = defaultSpeed * speedMultiplier;
            
            #if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = dir * spd;
            #else
                rb.velocity = dir * spd;
            #endif
        }

        public override void OnFixedUpdate(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier, float timeAlive, float seedX, float seedY, float seedZ, Vector3 initialPosition)
        {
            // Do nothing - physics engine handles the rest
        }

        public override void SimulatePath(List<Vector3> points, Vector3 origin, Vector3 forward, Vector3 up, Vector3 right, float speedMultiplier, float duration, float stepSize, float seedX, float seedY)
        {
            points.Clear();
            points.Add(origin);

            Vector3 currentPos = origin;
            Vector3 currentVel = forward * (defaultSpeed * speedMultiplier);
            Vector3 gravity = Physics.gravity;

            int steps = Mathf.CeilToInt(duration / Mathf.Max(0.001f, stepSize));

            for (int i = 0; i < steps; i++)
            {
                if (useGravity)
                    currentVel += gravity * stepSize;
                
                currentPos += currentVel * stepSize;
                points.Add(currentPos);
            }
        }
    }
}
