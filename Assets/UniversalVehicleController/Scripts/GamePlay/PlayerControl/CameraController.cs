using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FMODUnity;

namespace PG
{
    /// <summary>
    /// Move and rotation camera controller
    /// </summary>

    public class CameraController :InitializePlayer
    {
#pragma warning disable 0649

        [SerializeField] List<CameraPreset> CameraPresets = new List<CameraPreset>();
        [SerializeField] Transform HorizontalRotation;
        [SerializeField] Transform VerticalRotation;
        [SerializeField] Transform CameraViewTransform;                                 //Camera transform for change view.
        [SerializeField] Transform CameraShakeTransform;                                //Trasform for camera shake.
        [SerializeField] Camera MainCamera;

        [SerializeField] float ChangeCameraSpeed = 5;                                   //Camera switching speed for smooth switching.
        [SerializeField] float DellayAfterMouseMove = 5;                                //If there was a manual rotation of the camera, after passing this time, the camera starts to rotate automatically.
        [SerializeField] LayerMask ObstacleMask;                                        //A layer mask that the camera cannot pass through.
        [SerializeField] float DistanceToObstacle = 0.1f;
        [SerializeField] StudioEventEmitter SpeedWindEmitter;

        [Header ("SplitScreen settings")]
        [SerializeField] Vector2 CameraRectSize = new Vector2 (1, 0.5f);
        [SerializeField] Vector2 CameraRectPosP1 = new Vector2 (0, 0.5f);
        [SerializeField] Vector2 CameraRectPosP2 = new Vector2 (0, 0f);

#pragma warning restore 0649

        int ActivePresetIndex = -1;
        int CurrentFrame = 0;
        float TimerAfterMouseMove;
        float TargetHorizontalRotation;
        float LimitedHorizontalRotation;
        float TargetVerticalRotation;
        Coroutine SoftMoveCameraCoroutine;
        UserInput UserControl;
        float CarSpeedDelta;
        float PrevCarSpeed;
        int PlayerIndex = 0;

        //The target point is calculated from velocity of car.
        Vector3 _TargetPoint;
        Vector3 TargetPoint
        {
            get
            {
                if (CurrentFrame != Time.frameCount) //Condition for ignoring the calculation in one frame several times.
                {
                    if (Car == null || Car.RB == null)
                    {
                        return transform.position;
                    }

                    if (ActivePreset.IsFirstPerson)
                    {
                        _TargetPoint = Car.FirstPersonCameraPos.position;
                    }
                    else
                    {
                        _TargetPoint = Car.transform.TransformPoint (Car.Bounds.center);
                    }

                    CurrentFrame = Time.frameCount;
                }
                return _TargetPoint;
            }
        }
        public CameraPreset ActivePreset { get; private set; }

        Vector3 LocalCameraPos;
        Vector3 TargetShakeCameraPos;
        float RayDistance;
        RaycastHit RayHit;
        bool ManualRotation;
        FMOD.Studio.PARAMETER_ID SpeedID;

        public override bool Initialize (VehicleController vehicle)
        {
            if (Car != null)
            {
                Car.OnConnectTrailer -= SoftMoveCamera;
            }
            if (base.Initialize (vehicle) && Car)
            {
                Car.OnConnectTrailer += SoftMoveCamera;

                //Split screen logic
                if (GameController.SplitScreen)
                {
                    var rect = MainCamera.rect;
                    rect.size = CameraRectSize;
                    if (GameController.PlayerCar1 == Car)
                    {
                        rect.position = CameraRectPosP1;
                        PlayerIndex = 0;
                    }
                    else
                    {
                        rect.position = CameraRectPosP2;
                        PlayerIndex = 1;
                    }

                    MainCamera.rect = rect;
                }
            }

            return IsInitialized;
        }

        bool СameraWasRotatedManually
        {
            get { return TimerAfterMouseMove > 0; }
        }

        protected void Awake ()
        {
            CameraPresets.ForEach (p => p.Init ());

            ActivePresetIndex = 0;
            UpdateActiveCamera (fastCameraRotation: true);
        }

        private IEnumerator Start ()
        {
            if (TargetVehicle && !IsInitialized)
            {
                Initialize (TargetVehicle);
            }

            //Waiting for initialization.
            while (Vehicle == null)
            {
                yield return null;
            }

            transform.position = TargetPoint;
            
            TargetHorizontalRotation = 0;
            HorizontalRotation.rotation = Quaternion.identity;

            VerticalRotation.localRotation = Quaternion.identity;
            transform.rotation = Car.transform.rotation;

            Vehicle.ResetVehicleAction += OnResetCar;

            //Waiting for control initialization.
            while (Car.CarControl == null)
            {
                yield return null;
            }

            UserControl = Car.CarControl as UserInput;
            if (UserControl != null)
            {
                UserControl.OnChangeViewAction += SetNextCamera;
            }

            ActivePresetIndex = PlayerPrefs.GetInt ("CameraIndex" + PlayerIndex, 0);
            UpdateActiveCamera (fastCameraRotation: true);

            FMOD.Studio.PARAMETER_DESCRIPTION paramDescription;

            if (!SpeedWindEmitter.IsPlaying ())
            {
                SpeedWindEmitter.Play ();
            }
            SpeedWindEmitter.EventDescription.getParameterDescriptionByName ("Speed", out paramDescription);
            SpeedID = paramDescription.id;
        }

        private void FixedUpdate ()
        {
            var currentSpeed = Car.CurrentSpeed;
            CarSpeedDelta = Mathf.Lerp (CarSpeedDelta, currentSpeed - PrevCarSpeed, ActivePreset.GForceLerp);
            PrevCarSpeed = currentSpeed;
        }

        private void LateUpdate ()
        {
            if (ActivePreset.EnableRotation)
            {
                Vector2 mouseDelta = Vector2.zero;
                bool needRotate = Car.RB.velocity.ZeroHeight().sqrMagnitude > 0.1f;

                if (UserControl != null)
                {
                    mouseDelta = UserControl.ViewDelta;
                    if (Gamepad.current != null && UserControl.ViewDeltaDevice == Gamepad.current)
                    {
                        ManualRotation = !Mathf.Approximately (mouseDelta.sqrMagnitude, 0);
                    }
                    else
                    {
                        ManualRotation = UserControl.ManualCameraRotation;
                    }
                }

                if (ManualRotation)
                {
                    //Manual camera control.
                    if (!СameraWasRotatedManually)
                    {
                        TargetHorizontalRotation = 0;
                        TargetVerticalRotation = 0;
                    }
                    TargetHorizontalRotation += mouseDelta.x;
                    TargetVerticalRotation -= mouseDelta.y;

                    TargetVerticalRotation = Mathf.Clamp (TargetVerticalRotation, ActivePreset.MinVerticalAngle, ActivePreset.MaxVerticalAngle);

                    TimerAfterMouseMove = DellayAfterMouseMove;
                }
                else if (!СameraWasRotatedManually)
                {
                    //Automatic camera rotation
                    if (needRotate && Car.VehicleIsGrounded)
                    {
                        if (Car.CurrentGear < 0)
                        {
                            //Reverse logic
                            if (ActivePreset.IsFirstPerson && Car.FirstPersonCameraPos.localPosition.x > 0)
                            {
                                TargetHorizontalRotation = -180;
                            }
                            else
                            {
                                TargetHorizontalRotation = 180;
                            }
                        }
                        else if (Car.CurrentSpeed > 1)
                        {
                            //Turn the camera towards drift. 
                            TargetHorizontalRotation = Mathf.Lerp
                            (
                                Vehicle.VelocityAngle * ActivePreset.AdditionalRotationMultiplier,
                                Vehicle.VelocityAngle,
                                Mathf.InverseLerp (90, 180, Car.VelocityAngle.Abs ())
                            );
                        }
                    }
                    else
                    {
                        TargetHorizontalRotation = 0;
                    }

                    TargetVerticalRotation = 0;
                }
                else
                {
                    //Counter of the distance traveled by the car.
                    TimerAfterMouseMove -= Time.deltaTime;
                }

                if (ActivePreset.IsFirstPerson)
                {
                    transform.rotation = Quaternion.Lerp (transform.rotation, Car.FirstPersonCameraPos.rotation, ActivePreset.SetRotationSpeed * Time.deltaTime);
                }
                else
                {
                    Vector3 lookRotation;

                    if (ActivePreset.OnlyYRotation)
                    {
                        lookRotation = Car.transform.forward.ZeroHeight ();
                    }
                    else if (!Car.VehicleIsGrounded && Car.CurrentSpeed > 1 && needRotate)
                    {
                        //If the car is in the air, then the camera rotates towards Velocity
                        lookRotation = Car.RB.velocity;
                        lookRotation.y *= 0.5f;
                        lookRotation = Vector3.Lerp (lookRotation, Car.RB.velocity, Car.RB.velocity.normalized.y);
                    }
                    else
                    {
                        //If the car is on the ground, then the camera rotates towards Car.transform.forward
                        lookRotation = Car.transform.forward;
                        lookRotation.y *= 0.5f;
                        lookRotation = Vector3.Lerp (lookRotation, Car.transform.forward, Car.transform.forward.y);
                    }

                    transform.rotation = Quaternion.Lerp (transform.rotation, Quaternion.LookRotation(lookRotation, Vector3.up), ActivePreset.SetRotationSpeed * Time.deltaTime);
                }
            }
            else
            {
                TargetHorizontalRotation = 0;
                TargetVerticalRotation = 0;
                transform.rotation = Quaternion.Lerp (transform.rotation, Quaternion.identity, ActivePreset.SetRotationSpeed * Time.deltaTime);
            }

            //Applying camera movement and rotation.
            if (ActivePreset.IsFirstPerson)
            {
                if (TargetHorizontalRotation.Abs() < ActivePreset.LookingBackLimitAngle || TargetHorizontalRotation.Abs() != ActivePreset.LookingBackLimitAngle)
                {
                    LimitedHorizontalRotation = Mathf.Lerp (
                        LimitedHorizontalRotation,
                        TargetHorizontalRotation.Clamp (-ActivePreset.LookingBackLimitAngle, ActivePreset.LookingBackLimitAngle),
                        Time.deltaTime * ActivePreset.SetRotationSpeed);
                }
                
                HorizontalRotation.localRotation = Quaternion.AngleAxis (LimitedHorizontalRotation, Vector3.up);
                HorizontalRotation.localPosition = new Vector3 (ActivePreset.LookingBackXOffset * LimitedHorizontalRotation / ActivePreset.LookingBackLimitAngle, 0, 0);
            }
            else
            {
                HorizontalRotation.localRotation =
                        Quaternion.Lerp (HorizontalRotation.localRotation, Quaternion.Euler (0, TargetHorizontalRotation, 0), Time.deltaTime * ActivePreset.SetRotationSpeed);
            }
            VerticalRotation.localRotation =
                    Quaternion.Lerp (VerticalRotation.localRotation, Quaternion.Euler (TargetVerticalRotation, 0, 0), Time.deltaTime * ActivePreset.SetRotationSpeed);

            float speedFactor = (Car.CurrentSpeed / 100).Clamp();

            //Move camera
            transform.position = TargetPoint;
            if (ActivePreset.VelocityMultiplier > 0)
            {
                transform.position += Car.transform.forward * ActivePreset.VelocityMultiplier * speedFactor;
            }

            //FOV change logic, to change the feel of speed.
            var targetFov = Mathf.Lerp(ActivePreset.StandardFOV, ActivePreset.BoostFOV, Car.InBoost? Car.CurrentAcceleration: 0) + ActivePreset.SpeedFOVOffset * speedFactor;

            if (GameController.SplitScreen)
            {
                targetFov *= ActivePreset.SplitScreenFOVMultiplayer;
            }
            MainCamera.fieldOfView = Mathf.Lerp (MainCamera.fieldOfView, targetFov, ActivePreset.ChangeFovSpeed * Time.deltaTime);

            if (SoftMoveCameraCoroutine == null)
            {
                if (!CheckObstacles ())
                {
                    ApplyGForce ();
                }
            }

            UpdateEffects ();
        }

        private void OnDestroy ()
        {
            if (UserControl != null)
            {
                UserControl.OnChangeViewAction -= SetNextCamera;
            }

            if (Car)
            {
                Car.OnConnectTrailer -= SoftMoveCamera;
                Vehicle.ResetVehicleAction -= OnResetCar;
            }
        }

        void UpdateEffects ()
        {
            if (SpeedID.data1 != 0 || SpeedID.data2 != 0)
            {
                SpeedWindEmitter.SetParameter (SpeedID, Car.CurrentSpeed);
            }

            if (ActivePreset.EnableShake)
            {
                float shakePower = ((Car.CurrentSpeed - ActivePreset.MinSpeedForStartShake) / (ActivePreset.MaxSpeedForMaxShake - ActivePreset.MinSpeedForStartShake)).Clamp();
                if (Car.CurrentSpeed < ActivePreset.MinSpeedForStartShake)
                {
                    TargetShakeCameraPos = Vector3.zero;
                    shakePower = 1;
                }
                else if (CameraShakeTransform.localPosition == TargetShakeCameraPos)
                {

                    TargetShakeCameraPos = new Vector3 (
                        UnityEngine.Random.Range(-ActivePreset.ShakeCameraRadius.x, ActivePreset.ShakeCameraRadius.x) * shakePower,
                        UnityEngine.Random.Range (-ActivePreset.ShakeCameraRadius.y, ActivePreset.ShakeCameraRadius.y) * shakePower,
                        0
                    );
                }

                CameraShakeTransform.localPosition = Vector3.MoveTowards (CameraShakeTransform.localPosition, TargetShakeCameraPos, ActivePreset.ShakeSpeed * shakePower * Time.deltaTime);
            }
            else
            {
                CameraShakeTransform.localPosition = Vector3.zero;
            }
        }

        /// <summary>
        /// Switch to the next camera preset.
        /// </summary>
        public void SetNextCamera ()
        {
            ActivePresetIndex = MathExtentions.Repeat (ActivePresetIndex + 1, 0, CameraPresets.Count - 1);

            //First camera reset logic
            LimitedHorizontalRotation = 0;
            HorizontalRotation.localPosition = Vector3.zero;

            PlayerPrefs.SetInt ("CameraIndex" + PlayerIndex, ActivePresetIndex);

            SoftMoveCamera ();
        }

        public void SoftMoveCamera ()
        {
            UpdateActiveCamera (fastCameraRotation: false);
            if (SoftMoveCameraCoroutine != null)
            {
                StopCoroutine (SoftMoveCameraCoroutine);
            }

            SoftMoveCameraCoroutine = StartCoroutine (DoSoftMoveCamera ());
        }

        public void UpdateActiveCamera (bool fastCameraRotation)
        {
            ActivePreset = CameraPresets[ActivePresetIndex];

            if (ActivePreset.IsFirstPerson && Car.FirstPersonCameraPos == null)
            {
                SetNextCamera ();
                return;
            }

            if (fastCameraRotation)
            {
                if (ActivePreset.EnableRotation)
                {
                    TargetHorizontalRotation = 0;
                    HorizontalRotation.localRotation = Quaternion.identity;
                    TargetVerticalRotation = 0;
                    VerticalRotation.localRotation = Quaternion.identity;
                }

                float carSize = 0;
                if (Car)
                {
                    carSize = Car.Size;
                    if (Car.ConnectedTrailer)
                    {
                        carSize += Car.ConnectedTrailer.Size;
                    }
                }

                if (ActivePreset.IsFirstPerson)
                {
                    CameraViewTransform.localPosition = Vector3.zero;
                    CameraViewTransform.localRotation = Quaternion.identity;
                    LocalCameraPos = Vector3.zero;
                    RayDistance = 0;
                }
                else
                {
                    CameraViewTransform.localPosition = ActivePreset.GetLocalPosition (carSize);
                    CameraViewTransform.localRotation = ActivePreset.GetLocalRotation (carSize);
                    LocalCameraPos = CameraViewTransform.localPosition;
                    RayDistance = CameraViewTransform.localPosition.magnitude;
                }
            }
        }

        /// <summary>
        /// Smooth camera movement between presets
        /// </summary>
        IEnumerator DoSoftMoveCamera ()
        {
            Transform camTR = CameraViewTransform;
            float carSize = Car.Size;
            if (Car.ConnectedTrailer)
            {
                carSize += Car.ConnectedTrailer.Size;
            }

            //Select view pos for 1st or 3rd person.
            var targePos = ActivePreset.GetLocalPosition (carSize);
            var targetRot = ActivePreset.GetLocalRotation (carSize);

            Vector3 camPos = camTR.localPosition;
            Quaternion camRot = camTR.localRotation;

            while (camPos != targePos || camRot != targetRot)
            {
                camPos = Vector3.Lerp (camPos, targePos, Time.deltaTime * ChangeCameraSpeed);
                camRot = Quaternion.Lerp (camRot, targetRot, Time.deltaTime * ChangeCameraSpeed);

                camTR.localPosition = camPos;
                camTR.localRotation = camRot;

                LocalCameraPos = camTR.localPosition;
                RayDistance = camTR.localPosition.magnitude + DistanceToObstacle;

                if (!CheckObstacles ())
                {
                    ApplyGForce ();
                }

                yield return new WaitForEndOfFrame();
            }

            RayDistance = camTR.localPosition.magnitude + DistanceToObstacle;
            SoftMoveCameraCoroutine = null;
        }

        bool CheckObstacles ()
        {
            if (ActivePreset.IsFirstPerson)
            {
                CameraViewTransform.localPosition = LocalCameraPos;
                return false;
            }
            var position = transform.position;
            var direction = (CameraViewTransform.position - transform.position).normalized;
            if (Physics.Raycast(position, direction, out RayHit, RayDistance, ObstacleMask)) 
            {
                CameraViewTransform.position = Vector3.MoveTowards (RayHit.point, transform.position, DistanceToObstacle);
                return true;
            }
            else
            {
                CameraViewTransform.localPosition = LocalCameraPos;
                return false;
            }
        }

        void ApplyGForce ()
        {
            if (ActivePreset.EnableGForceOffset)
            {
                var localPos = CameraViewTransform.localPosition;
                localPos.z -= CarSpeedDelta > 0? (CarSpeedDelta * ActivePreset.AccelerationGForceMultiplier): (CarSpeedDelta * ActivePreset.BrakeGForceMultiplier);
                CameraViewTransform.localPosition = localPos;
            }
        }

        /// <summary>
        /// Instant change of position and rotation.
        /// </summary>
        void OnResetCar ()
        {
            transform.position = TargetPoint;
            if (ActivePreset.EnableRotation && !СameraWasRotatedManually)
            {
                UpdateActiveCamera (fastCameraRotation: true);
            }
        }

        private void OnDrawGizmosSelected ()
        {
            Gizmos.color = Color.red;

            Gizmos.DrawWireSphere (TargetPoint, 1);

            Gizmos.color = Color.white;
        }

        [System.Serializable]
        public class CameraPreset
        {
#pragma warning disable 0649

            [SerializeField] string PresetName;                 //To display the name in the editor
            [Header("Dependence on the size of the car")]
            public bool IsFirstPerson;
            [ShowInInspectorIf("IsFirstPerson")] public float LookingBackLimitAngle = 170;                  //Horizontal rotation limit (For the camera from the first person).
            [ShowInInspectorIf("IsFirstPerson")] public float LookingBackXOffset = 0.15f;                   //Horizontal offset when looking back (For the camera from the first person).
            [SerializeField, HideInInspectorIf("IsFirstPerson")] Transform MinCameraLocalPosition;          //Parent fo camera position, destroyed after initialization.
            [SerializeField, HideInInspectorIf("IsFirstPerson")] Transform MaxCameraLocalPosition;          //Parent fo camera position, destroyed after initialization.

#pragma warning restore 0649

            [HideInInspectorIf ("IsFirstPerson")] public float MinTargetVehicleSize = 5f;
            [HideInInspectorIf ("IsFirstPerson")] public float MaxTargetVehicleSize = 40f;

            [Header("Move Settings")]
            public float VelocityMultiplier;

            public bool EnableGForceOffset;
            [ShowInInspectorIf("EnableGForceOffset")] public float AccelerationGForceMultiplier = 20;       //The multiplier to move the camera (Back) when accelerating.
            [ShowInInspectorIf("EnableGForceOffset")] public float BrakeGForceMultiplier = 10;              //The force to move the camera (Forward) when braking.
            [ShowInInspectorIf("EnableGForceOffset")] public float GForceLerp = 0.01f;                      //Offset interpolation for smoothness.

            [Header("Rotation Settings")]
            public bool EnableRotation;
            [ShowInInspectorIf("EnableRotation")] public bool OnlyYRotation;
            [ShowInInspectorIf("EnableRotation")] public float MinVerticalAngle = -15;
            [ShowInInspectorIf("EnableRotation")] public float MaxVerticalAngle = 40;
            [ShowInInspectorIf("EnableRotation")] public float SetRotationSpeed = 5;                        //Change rotation speed.
            [ShowInInspectorIf("EnableRotation")] public float AdditionalRotationMultiplier = 0.5f;

            [Header("FOV Settings")]
            public float StandardFOV = 60;
            public float BoostFOV = 75;
            public float SpeedFOVOffset = 30;
            public float ChangeFovSpeed = 5;
            public float SplitScreenFOVMultiplayer = 0.66f;

            [Header("Shake Settings")]
            public bool EnableShake = true;
            [ShowInInspectorIf("EnableShake")] public Vector2 ShakeCameraRadius = new Vector3 (0.08f, 0.08f);
            [ShowInInspectorIf("EnableShake")] public float ShakeSpeed = 1;
            [ShowInInspectorIf("EnableShake")] public float MinSpeedForStartShake = 15;
            [ShowInInspectorIf("EnableShake")] public float MaxSpeedForMaxShake = 60;

            Vector3 GetMinLocalPosition = Vector3.zero;
            Quaternion GetMinLocalRotation = Quaternion.identity;

            Vector3 GetMaxLocalPosition = Vector3.zero;
            Quaternion GetMaxLocalRotation = Quaternion.identity;

            public void Init ()
            {
                if (IsFirstPerson)
                {
                    if (MinCameraLocalPosition)
                    {
                        GameObject.Destroy (MinCameraLocalPosition.gameObject);
                    }
                    if (MaxCameraLocalPosition)
                    {
                        GameObject.Destroy (MaxCameraLocalPosition.gameObject);
                    }
                }
                else
                {
                    if (MinCameraLocalPosition)
                    {
                        GetMinLocalPosition = MinCameraLocalPosition.localPosition;
                        GetMinLocalRotation = MinCameraLocalPosition.localRotation;

                        GameObject.Destroy (MinCameraLocalPosition.gameObject);
                    }

                    if (MaxCameraLocalPosition)
                    {
                        GetMaxLocalPosition = MaxCameraLocalPosition.localPosition;
                        GetMaxLocalRotation = MaxCameraLocalPosition.localRotation;
                        GameObject.Destroy (MaxCameraLocalPosition.gameObject);
                    }
                    else
                    {
                        GetMaxLocalPosition = GetMinLocalPosition;
                        GetMaxLocalRotation = GetMinLocalRotation;
                    }
                }
            }

            public Vector3 GetLocalPosition (float targetSize)
            {
                if (IsFirstPerson)
                {
                    return Vector3.zero;
                }
                targetSize = targetSize.Clamp (MinTargetVehicleSize, MaxTargetVehicleSize);
                var t = (targetSize - MinTargetVehicleSize) / (MaxTargetVehicleSize - MinTargetVehicleSize);
                return Vector3.Lerp (GetMinLocalPosition, GetMaxLocalPosition, t);
            }

            public Quaternion GetLocalRotation (float targetSize)
            {
                if (IsFirstPerson)
                {
                    return Quaternion.identity;
                }
                targetSize = targetSize.Clamp (MinTargetVehicleSize, MaxTargetVehicleSize);
                var t = (targetSize - MinTargetVehicleSize) / (MaxTargetVehicleSize - MinTargetVehicleSize);
                return Quaternion.Lerp (GetMinLocalRotation, GetMaxLocalRotation, t);
            }
        }
    }
}
