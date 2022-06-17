using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PG
{
    public class LookAtTransform :MonoBehaviour
    {
#pragma warning disable 0649

        [SerializeField] Transform TargetTransform;
        [SerializeField] Vector3 LocalPositionOffset;
        [SerializeField] Vector3 RotationOffset;

        Transform ParentTransform;

#pragma warning restore 0649

        void LateUpdate ()
        {
            transform.localRotation = Quaternion.LookRotation (transform.parent.InverseTransformDirection(TargetTransform.position - transform.position) + LocalPositionOffset, Vector3.up) * Quaternion.Euler (RotationOffset);
        }

#if UNITY_EDITOR

        [ContextMenu("UpdateTransform")]
        public void UpdateTransform ()
        {
            LateUpdate();
        }

#endif
    }

#if UNITY_EDITOR

    [CustomEditor (typeof (LookAtTransform))]
    [CanEditMultipleObjects]
    public class LookAtTransformEditor :Editor
    {
        SerializedProperty lookAtPoint;

        void OnEnable ()
        {
            lookAtPoint = serializedObject.FindProperty ("lookAtPoint");
        }

        public override void OnInspectorGUI ()
        {
            base.OnInspectorGUI ();
            if (GUILayout.Button ("UpdateTransform"))
            {
                (target as LookAtTransform).UpdateTransform ();
            }
        }
    }

#endif

}
