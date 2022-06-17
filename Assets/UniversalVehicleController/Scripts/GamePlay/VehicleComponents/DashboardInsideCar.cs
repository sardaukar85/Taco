using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PG
{
    /// <summary>
    /// To display the dashboard arrows. Stops working if damaged.
    /// </summary>
    public class DashboardInsideCar :DamageableObject
    {
        public Transform SpeedArrow;        //Speed Transform rotates on the z-axis
        public float MaxSpeed = 280;
        public float MinSpeedAngle = 30;
        public float MaxSpeedAngle = 240;

        public Transform RPMArrow;          //RPM Transform rotates on the z-axis
        public float MinRPMAngle = 30;
        public float MaxRPMAngle = 240;

        CarController Car;

        private void Start ()
        {
            Car = GetComponentInParent<CarController> ();
            if (!Car)
            {
                Debug.LogError ("Car in parent not found");
                Destroy (gameObject);
            }
        }

        private void Update ()
        {
            if (!IsDead)
            {
                float arrowAngle = Mathf.Lerp (MinSpeedAngle, MaxSpeedAngle, Mathf.InverseLerp (0, MaxSpeed, Car.CurrentSpeed));
                SpeedArrow.localRotation = Quaternion.AngleAxis (arrowAngle, Vector3.forward);

                arrowAngle = Mathf.Lerp (MinRPMAngle, MaxRPMAngle, Mathf.InverseLerp (0, Car.Engine.MaxRPM, Car.EngineRPM));
                RPMArrow.localRotation = Quaternion.AngleAxis (arrowAngle, Vector3.forward);
            }
        }

        private void OnDisable ()
        {
            SpeedArrow.localRotation = Quaternion.AngleAxis (MinSpeedAngle, Vector3.forward);
            RPMArrow.localRotation = Quaternion.AngleAxis (MinRPMAngle, Vector3.forward);
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(DashboardInsideCar))]
    public class DashboardInsideCarEditor: Editor
    {
        public override void OnInspectorGUI ()
        {
            base.OnInspectorGUI ();

            var dashboard = target as DashboardInsideCar;
            EditorGUILayout.Space (10);
            EditorGUILayout.HelpBox (string.Format ("mps = {0}\nkph = {1}\nmph = {2}", dashboard.MaxSpeed, dashboard.MaxSpeed * C.KPHMult, dashboard.MaxSpeed * C.MPHMult), MessageType.Info);
        }
    }

#endif
}
