using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.ParticleSystem;

namespace PG
{
    /// <summary>
    /// Wrapper for WheelCollider. The current slip, temperature, surface, etc. is calculated.
    /// </summary>
    [RequireComponent (typeof (WheelCollider))]
    public class Wheel :MoveableDO
    {
        [Range(-1f, 1f)]
        public float SteerPercent;                  //Percentage of wheel turns, 1 - maximum possible turn CarController.Steer.MaxSteerAngle, -1 negative wheel turn (For example, to turn the rear wheels).
        public bool DriveWheel;
        public float MaxBrakeTorque;
        public bool HandBrakeWheel;
        public Transform WheelView;                 //The transform of which takes the position and rotation of the wheel.
        public Transform WheelHub;                  //The transform which occupies rotation only along the Y axis of the wheel.
        public float MaxVisualDamageAngle = 5f;     //The maximum offset angle for children to visualize damage.
        [Range(0, 1)]
        public float AntiRollBar;

        public Wheel AntiRollWheel;

        [Tooltip("The angle at which the wheel leans at the extreme points of the suspension (Only visual effect)")]
        public float MaxSuspensionWheelAngle;
        [Tooltip("If the suspension is dependent, then the angle of the wheel depends on the opposite wheel (Only visual effect)")]
        public bool DependentSuspension;

        public float RPM { get { return WheelCollider.rpm; } }
        public float CurrentMaxSlip { get { return Mathf.Max (CurrentForwardSlip, CurrentSidewaysSlip); } }
        public float CurrentForwardSlip { get; private set; }
        public float CurrentSidewaysSlip { get; private set; }
        public float SlipNormalized { get; private set; }
        public float ForwardSlipNormalized { get; private set; }
        public float SidewaysSlipNormalized { get; private set; }
        public float SuspensionPos { get; private set; } = 0;
        public float PrevSuspensionPos { get; private set; } = 0;
        public float SuspensionPosDiff { get; private set; } = 0;
        public float WheelTemperature { get; private set; }             //Temperature for visualizing tire smoke.
        public bool HasForwardSlip { get { return CurrentForwardSlip > WheelCollider.forwardFriction.asymptoteSlip; } }
        public bool HasSideSlip { get { return CurrentSidewaysSlip > WheelCollider.sidewaysFriction.asymptoteSlip; } }
        public WheelHit GetHit { get { return Hit; } }
        public Vector3 HitPoint { get; private set; }
        public bool IsGrounded { get { return !IsDead && WheelCollider.isGrounded; } }
        public bool StopEmitFX { get; set; }
        public float Radius { get { return WheelCollider.radius; } }
        public Vector3 LocalPositionOnAwake { get; private set; }       //For CarState
        
        public bool IsSteeringWheel { get { return !Mathf.Approximately (0, SteerPercent); } }

        Transform[] ViewChilds;
        public WheelCollider WheelCollider { get; protected set; }

        [System.NonSerialized]
        public Vector3 Position;
        [System.NonSerialized]
        public Quaternion Rotation;

        Vector3 LocalPosition;

        protected VehicleController Vehicle;
        protected WheelHit Hit;
        GroundConfig DefaultGroundConfig { get { return GroundDetection.GetDefaultGroundConfig; } }
        protected float CurrentRotateAngle;

        const float TemperatureChangeSpeed = 0.1f;
        float GroundStiffness;
        float BrakeSpeed = 2;
        float CurrentBrakeTorque;

        GroundConfig _CurrentGroundConfig;
        public GroundConfig CurrentGroundConfig         //When the ground changes, the grip of the wheels changes.
        { 
            get 
            { 
                return _CurrentGroundConfig; 
            }
            set
            {
                if (_CurrentGroundConfig != value)
                {
                    _CurrentGroundConfig = value;
                    if (_CurrentGroundConfig != null)
                    {
                        GroundStiffness = _CurrentGroundConfig.WheelStiffness;
                    }
                }
            }
        }

        public override void Awake ()
        {
            Vehicle = GetComponentInParent<VehicleController> ();
            if (Vehicle == null)
            {
                Debug.LogError ("[Wheel] Parents without CarController");
                Destroy (this);
            }

            WheelCollider = GetComponent<WheelCollider> ();
            WheelCollider.ConfigureVehicleSubsteps (40, 13, 8);

            LocalPositionOnAwake = transform.localPosition;

            InitDamageObject ();

            ViewChilds = new Transform[WheelView.childCount];
            for (int i = 0; i < ViewChilds.Length; i++)
            {
                ViewChilds[i] = WheelView.GetChild (i);
            }

            CurrentGroundConfig = DefaultGroundConfig;

            Vehicle.ResetVehicleAction += OnResetAction;

            WheelCollider.GetWorldPose (out Position, out Rotation);
            LocalPosition = Position - transform.position;
            SuspensionPos = Mathf.InverseLerp (WheelCollider.center.y - WheelCollider.suspensionDistance, WheelCollider.center.y, LocalPosition.y);
            PrevSuspensionPos = SuspensionPos;
        }

        /// <summary>
        /// Update gameplay logic.
        /// </summary>
        public void FixedUpdate ()
        {
            float targetTemperature = 0;

            WheelCollider.GetWorldPose (out Position, out Rotation);
            LocalPosition = transform.InverseTransformPoint(Position);

            if (WheelCollider.GetGroundHit (out Hit))
            {
                //Calculation of the current friction.
                CurrentForwardSlip = (CurrentForwardSlip + Mathf.Abs (Hit.forwardSlip)) / 2;
                CurrentSidewaysSlip = (CurrentSidewaysSlip + Mathf.Abs (Hit.sidewaysSlip)) / 2;

                HitPoint = Hit.point;

                ForwardSlipNormalized = CurrentForwardSlip / WheelCollider.forwardFriction.extremumSlip;
                SidewaysSlipNormalized = CurrentSidewaysSlip / WheelCollider.sidewaysFriction.extremumSlip;

                SlipNormalized = Mathf.Max(ForwardSlipNormalized, SidewaysSlipNormalized);

                //Determining the type of surface under the wheel.
                var groundEntity = GroundDetection.GetGroundEntity(Hit.collider.gameObject);
                GroundConfig groundConfig = DefaultGroundConfig;

                if (groundEntity != null)
                {
                    groundConfig = groundEntity.GetGroundConfig (Hit.point);
                }

                targetTemperature = HasForwardSlip || HasSideSlip ? 1 : 0;
                CurrentGroundConfig = groundConfig;

                PrevSuspensionPos = SuspensionPos;
                SuspensionPos = Mathf.InverseLerp (WheelCollider.center.y - WheelCollider.suspensionDistance, WheelCollider.center.y, LocalPosition.y);
                SuspensionPosDiff = SuspensionPos - PrevSuspensionPos;
            }
            else
            {
                CurrentForwardSlip = 0;
                CurrentSidewaysSlip = 0;
                ForwardSlipNormalized = 0;
                SidewaysSlipNormalized = 0;
                SlipNormalized = 0;
                CurrentGroundConfig = DefaultGroundConfig;
                SuspensionPos = 0;
                SuspensionPosDiff = 0;
                PrevSuspensionPos = 0;
            }

            WheelTemperature = Mathf.MoveTowards (WheelTemperature, targetTemperature, Time.fixedDeltaTime * TemperatureChangeSpeed);

            ApplyStiffness ();
            ApplyBrake ();
            ApplyAntiRollForce ();
        }

        /// <summary>
        /// Denamichisky change Stiffness. forwardFriction affects sidewaysFriction
        /// </summary>
        void ApplyStiffness ()
        {
            float stiffness = GroundStiffness;
            var friction = WheelCollider.forwardFriction;
            friction.stiffness = stiffness;
            WheelCollider.forwardFriction = friction;

            friction = WheelCollider.sidewaysFriction;
            friction.stiffness = stiffness * Mathf.Lerp(0.3f, 1, Mathf.InverseLerp (2, 1, ForwardSlipNormalized));
            WheelCollider.sidewaysFriction = friction;
        }

        /// <summary>
        /// Apply braking with a slight delay.
        /// </summary>
        void ApplyBrake ()
        {
            if (CurrentBrakeTorque > WheelCollider.brakeTorque)
            {
                WheelCollider.brakeTorque = Mathf.Lerp (WheelCollider.brakeTorque, CurrentBrakeTorque, BrakeSpeed * Time.fixedDeltaTime);
            }
            else
            {
                WheelCollider.brakeTorque = CurrentBrakeTorque;
            }
        }

        //
        void ApplyAntiRollForce ()
        {
            if (IsGrounded && AntiRollWheel)
            {
                float susDiff = (SuspensionPos - AntiRollWheel.SuspensionPos) * AntiRollBar * 3;
                WheelCollider.attachedRigidbody.AddForceAtPosition (transform.up * WheelCollider.attachedRigidbody.mass * susDiff, transform.position);
            }
        }

        /// <summary>
        /// Update visual logic (Transform).
        /// </summary>
        public virtual void LateUpdate ()
        {
            if (Vehicle.VehicleIsVisible)
            {
                WheelCollider.GetWorldPose (out Position, out Rotation);
                Vector3 hubLocalPosition = Vector3.Lerp (WheelView.localPosition, LocalPosition, 0.3f);

                WheelView.localPosition = hubLocalPosition;
                CurrentRotateAngle += RPM * Time.deltaTime * 6.28f;
                float forwardAngle;
                if (!DependentSuspension || AntiRollWheel == null)
                {
                    //Imitation dependent suspension
                    forwardAngle = (SuspensionPos - 0.5f) * MaxSuspensionWheelAngle;
                    if (transform.localPosition.x < 0)
                    {
                        forwardAngle *= -1;
                    }
                }
                else if (transform.localPosition.x < AntiRollWheel.transform.localPosition.x)
                {
                    //Imitation independent suspension for left wheel.
                    forwardAngle = (AntiRollWheel.SuspensionPos - SuspensionPos) * 0.5f * MaxSuspensionWheelAngle;
                }
                else
                {
                    //Imitation independent suspension for right wheel.
                    forwardAngle = (SuspensionPos - AntiRollWheel.SuspensionPos) * 0.5f * MaxSuspensionWheelAngle;
                }

                Quaternion hubAngle = Quaternion.AngleAxis (forwardAngle, Vector3.forward);
                hubAngle *= Quaternion.AngleAxis (WheelCollider.steerAngle, Vector3.up);

                WheelView.localRotation = hubAngle;
                WheelView.localRotation *= Quaternion.AngleAxis (CurrentRotateAngle, Vector3.right);
                if (WheelHub)
                {
                    WheelHub.localPosition = hubLocalPosition;
                    WheelHub.localRotation = hubAngle;
                }
            }
        }

        /// <summary>
        /// Transfer torque to the wheels.
        /// </summary>
        /// <param name="motorTorque">Motor torque</param>
        /// <param name="forceSetTroque">Force torque transfer ignoring the DriveWheel flag.</param>
        public void SetMotorTorque (float motorTorque, bool forceSetTroque = false)
        {
            if (DriveWheel || forceSetTroque)
            {
                WheelCollider.motorTorque = motorTorque;
            }
        }

        public void SetSteerAngle (float steerAngle)
        {
            if (IsSteeringWheel)
            {
                WheelCollider.steerAngle = steerAngle * SteerPercent;
            }
        }

        public void SetHandBrake (bool handBrake)
        {
            if (HandBrakeWheel && handBrake)
            {
                CurrentBrakeTorque = MaxBrakeTorque;
            }
            else
            {
                CurrentBrakeTorque = 0;
            }
        }

        public void SetBrakeTorque (float brakeTorque)
        {
            CurrentBrakeTorque = brakeTorque * MaxBrakeTorque;
        }

        /// <summary>
        /// Dealing damage. At full damage, the wheel is separated from the car.
        /// </summary>
        public override void SetDamage (float damage)
        {
            damage = GetClampedDamage (damage);
            base.SetDamage (damage);

            var rotation = Quaternion.AngleAxis(MaxVisualDamageAngle * (damage / InitHealth), Vector3.up);
            foreach (var viewChild in ViewChilds)
            {
                viewChild.localRotation *= rotation;
            }
        }

        void OnResetAction ()
        {
            CurrentForwardSlip = 0;
            CurrentSidewaysSlip = 0;
            SlipNormalized = 0;
            CurrentGroundConfig = DefaultGroundConfig;
        }

        /// <summary>
        /// Detach wheel, enable collider and Rigidbody.
        /// </summary>
        public override void DoDeath ()
        {
            base.DoDeath ();

            CurrentForwardSlip = 0;
            CurrentSidewaysSlip = 0;
            SlipNormalized = 0;
            Hit = new WheelHit ();

            enabled = false;
            WheelCollider.enabled = false;
            this.enabled = false;

            WheelView.parent = null;
            var meshCollider = WheelView.GetComponentInChildren<MeshCollider>(true);
            if (meshCollider)
            {
                meshCollider.enabled = true;
            }

            var wheelRB = WheelView.gameObject.AddComponent<Rigidbody> ();
            wheelRB.mass = WheelCollider.mass;
        }
    }
}
