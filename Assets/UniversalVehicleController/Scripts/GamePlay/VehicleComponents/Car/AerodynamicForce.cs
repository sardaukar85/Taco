using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PG
{
    /// <summary>
    /// To simulate downforce (For example: hood, bumper, spoiler).
    /// </summary>
    public class AerodynamicForce :MonoBehaviour
    {
        public float MaxForvardForce = 0;
        public float MaxUpForce = -1000;
        public float MaxSpeed = 100;

        VehicleController Vehicle;

        private void Start ()
        {
            Vehicle = GetComponentInParent<VehicleController> ();

            if (Vehicle == null)
            {
                Debug.LogError ("VehicleController not found");
                Destroy (this);
            }
        }

        private void FixedUpdate ()
        {
            var force = transform.TransformDirection (new Vector3 (0, MaxUpForce, MaxForvardForce)) * (Vehicle.CurrentSpeed / MaxSpeed).Clamp();
            Vehicle.RB.AddForceAtPosition (force, transform.position);
        }
    }
}
