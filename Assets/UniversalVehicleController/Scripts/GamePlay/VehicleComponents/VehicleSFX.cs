using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using System.Linq;

namespace PG
{
    /// <summary>
    /// Sound effects, using FMOD.
    /// </summary>
    public class VehicleSFX :MonoBehaviour
    {
        [Header("VehicleSFX")]

        [Header("Suspension sounds")]
        [SerializeField] StudioEventEmitter SuspensionEmitter;                              //Suspension emitter, for playing suspension sounds.

        [Header("Ground effects")]
        [SerializeField] StudioEventEmitter WheelsEffectEmitterRef;                         //Wheel emitter, for playing slip sounds.

        [Header("Collisions")]
        [SerializeField] float MinTimeBetweenCollisions = 0.1f;
        [SerializeField] float DefaultMagnitudeDivider = 20;                                //default divider to calculate collision volume.
        [SerializeField] EventReference DefaultCollisionEventRef;                           //Event playable if the desired one was not found.      
        [SerializeField] List<ColissionEvent> CollisionEvents = new List<ColissionEvent>();

        [Header("Frictions")]
        [SerializeField] StudioEventEmitter FrictionEffectEmotterRef;
        [SerializeField] float PlayFrictionTime = 0.5f;
        [SerializeField] EventReference DefaultFrictionEventRef;                            //Event playable if the desired one was not found.                        
        [SerializeField] List<ColissionEvent> FrictionEvents = new List<ColissionEvent>();

#pragma warning restore 0649

        //PARAMETER_ID to not use a strings when calling "SetParameter" methods.
        static FMOD.Studio.PARAMETER_ID SlipID;
        static FMOD.Studio.PARAMETER_ID SpeedID;
        static FMOD.Studio.PARAMETER_ID FrictionTimeID;
        static FMOD.Studio.PARAMETER_ID SuspensionForceID;
        static FMOD.Studio.PARAMETER_ID SuspensionPosID;

        Dictionary<FMOD.GUID, WheelSoundData> WheelSounds = new Dictionary<FMOD.GUID, WheelSoundData>();              //Dictionary for playing multiple wheel sounds at the same time.\
        Dictionary<EventReference, FrictionSoundData> FrictionSounds = new Dictionary<EventReference, FrictionSoundData>();     //Dictionary for playing multiple friction sounds at the same time.

        protected VehicleController Vehicle;
        EventReference CurrentFrictionEvent;
        float LastCollisionTime;

        protected event System.Action UpdateAction;

        protected virtual void Start ()
        {
            Vehicle = GetComponentInParent<VehicleController> ();

            if (Vehicle == null)
            {
                Debug.LogErrorFormat ("[{0}] VehicleSFX without VehicleController in parent", name);
                enabled = false;
                return;
            }

            //Subscribe to collisions.
            Vehicle.CollisionAction += PlayCollisionSound;
            Vehicle.CollisionStayAction += PlayCollisionStayAction;

            //Get PARAMETER_ID for all the necessary events.
            FMOD.Studio.PARAMETER_DESCRIPTION paramDescription;

            WheelsEffectEmitterRef.EventDescription.getParameterDescriptionByName ("Slip", out paramDescription);
            SlipID = paramDescription.id;

            WheelsEffectEmitterRef.EventDescription.getParameterDescriptionByName ("Speed", out paramDescription);
            SpeedID = paramDescription.id;

            FrictionEffectEmotterRef.EventDescription.getParameterDescriptionByName ("Time", out paramDescription);
            FrictionTimeID = paramDescription.id;

            //Setting default values.
            WheelsEffectEmitterRef.SetParameter (SlipID, 0);
            WheelsEffectEmitterRef.SetParameter (SpeedID, 0);
            FrictionEffectEmotterRef.SetParameter (SpeedID, 0);
            FrictionEffectEmotterRef.SetParameter (FrictionTimeID, 0);

            StartCoroutine (InitSuspensionSounds());

            WheelSounds.Add (WheelsEffectEmitterRef.EventReference.Guid, new WheelSoundData () { Emitter = WheelsEffectEmitterRef });

            FrictionSounds.Add (FrictionEffectEmotterRef.EventReference, new FrictionSoundData () { Emitter = FrictionEffectEmotterRef, LastFrictionTime = Time.time });
            FrictionEffectEmotterRef.Stop ();

            UpdateAction += UpdateWheels;
            UpdateAction += UpdateFrictions;
        }

        /// <summary>
        /// To delay the suspension sound logic, otherwise the suspension sound will be played at start.
        /// </summary>
        /// <returns></returns>
        IEnumerator InitSuspensionSounds ()
        {
            yield return new WaitForSeconds (0.5f);

            FMOD.Studio.PARAMETER_DESCRIPTION paramDescription;

            if (SuspensionEmitter)
            {
                SuspensionEmitter.EventDescription.getParameterDescriptionByName ("SuspensionForce", out paramDescription);
                SuspensionForceID = paramDescription.id;

                SuspensionEmitter.EventDescription.getParameterDescriptionByName ("SuspensionPos", out paramDescription);
                SuspensionPosID = paramDescription.id;

                UpdateAction += UpdateSuspension;
            }
        }

        protected virtual void Update ()
        {
            UpdateAction.SafeInvoke ();
        }

        private void OnDestroy ()
        {
            foreach (var soundKV in WheelSounds)
            {
                if (soundKV.Value.Emitter)
                {
                    soundKV.Value.Emitter.Stop ();
                }
            }

            foreach (var soundKV in FrictionSounds)
            {
                if (soundKV.Value.Emitter)
                {
                    soundKV.Value.Emitter.Stop ();
                }
            }
        }

        void UpdateWheels ()
        {
            //Wheels sounds logic.
            //Find the sound for each wheel.

            foreach (var wheel in Vehicle.Wheels)
            {
                if (wheel.IsDead)
                {
                    continue;
                }

                WheelSoundData sound = null;

                if (!WheelSounds.TryGetValue (wheel.CurrentGroundConfig.EventRef.Guid, out sound))
                {
                    var emitter = WheelsEffectEmitterRef.gameObject.AddComponent<StudioEventEmitter>();
                    emitter.EventReference = wheel.CurrentGroundConfig.EventRef;
                    emitter.PlayEvent = WheelsEffectEmitterRef.PlayEvent;
                    emitter.StopEvent = WheelsEffectEmitterRef.StopEvent;
                    sound = new WheelSoundData ()
                    {
                        Emitter = emitter
                    };
                    WheelSounds.Add (wheel.CurrentGroundConfig.EventRef.Guid, sound);
                }

                sound.WheelsCount++;

                //Find the maximum slip for each sound.
                if (wheel.SlipNormalized > sound.Slip)
                {
                    sound.Slip = wheel.SlipNormalized;
                }
            }

            foreach (var sound in WheelSounds)
            {
                //Play or stop events.
                if (sound.Value.WheelsCount == 0)
                {
                    sound.Value.Emitter.Stop ();
                }
                else if (!sound.Value.Emitter.IsPlaying ())
                {
                    sound.Value.Emitter.Play ();
                }

                //Passing parameters to events.
                sound.Value.Emitter.SetParameter (SlipID, sound.Value.Slip);
                sound.Value.Emitter.SetParameter (SpeedID, Vehicle.CurrentSpeed);
                sound.Value.Slip = 0;
                sound.Value.WheelsCount = 0;
            }
        }

        void UpdateFrictions ()
        {
            FrictionSoundData soundData;
            foreach (var sound in FrictionSounds)
            {
                soundData = sound.Value;
                if (soundData.Emitter.IsPlaying ())
                {
                    var time = Time.time - soundData.LastFrictionTime;

                    if (time > PlayFrictionTime)
                    {
                        soundData.Emitter.SetParameter (SpeedID, 0);
                        soundData.Emitter.SetParameter (FrictionTimeID, PlayFrictionTime);
                        soundData.Emitter.Stop ();
                    }
                    else
                    {
                        soundData.Emitter.SetParameter (SpeedID, Vehicle.CurrentSpeed);
                        soundData.Emitter.SetParameter (FrictionTimeID, time);
                    }
                }
            }
        }

        void UpdateSuspension ()
        {
            Wheel maxSuspensionForceWheel = Vehicle.Wheels[0];
            for (int i = 1; i < Vehicle.Wheels.Length; i++)
            {
                if (Vehicle.Wheels[i].SuspensionPosDiff > maxSuspensionForceWheel.SuspensionPosDiff)
                {
                    maxSuspensionForceWheel = Vehicle.Wheels[i];
                }
            }

            float suspensionForce = maxSuspensionForceWheel.SuspensionPosDiff * maxSuspensionForceWheel.WheelCollider.suspensionDistance * 10;

            if (!SuspensionEmitter.IsPlaying ())
            {
                SuspensionEmitter.Play ();
            }
            SuspensionEmitter.SetParameter (SuspensionForceID, suspensionForce);
        }

        #region Collisions

        /// <summary>
        /// Play collision stay sound.
        /// </summary>
        public void PlayCollisionStayAction (VehicleController vehicle, Collision collision)
        {
            if (Vehicle.CurrentSpeed >= 1 && (collision.rigidbody == null || (collision.rigidbody.velocity - vehicle.RB.velocity).sqrMagnitude > 25))
            {
                PlayFrictionSound (collision, collision.relativeVelocity.magnitude);
            }
        }

        /// <summary>
        /// Play collision sound.
        /// </summary>
        public void PlayCollisionSound (VehicleController vehicle, Collision collision)
        {
            if (!vehicle.VehicleIsVisible || collision == null)
                return;

            var collisionLayer = collision.gameObject.layer;

            if (Time.time - LastCollisionTime < MinTimeBetweenCollisions)
            {
                return;
            }

            LastCollisionTime = Time.time;
            float collisionMagnitude = 0;
            if (collision.rigidbody)
            {
                collisionMagnitude = (Vehicle.RB.velocity - collision.rigidbody.velocity).magnitude;
            }
            else
            {
                collisionMagnitude = collision.relativeVelocity.magnitude;
            }
            float magnitudeDivider;

            var soundEvent = GetEventForCollision (collisionLayer, collisionMagnitude, out magnitudeDivider);

            var volume = Mathf.Clamp01 (collisionMagnitude / magnitudeDivider.Clamp(0, 40));

            FMODExtentions.PlayOneShot (soundEvent, volume, collision.contacts[0].point, Vehicle.RB.velocity);
        }

        void PlayFrictionSound (Collision collision, float magnitude)
        {
            if (Vehicle.CurrentSpeed >= 1)
            {
                CurrentFrictionEvent = GetEventForFriction (collision.collider.gameObject.layer, magnitude);

                FrictionSoundData soundData;
                if (!FrictionSounds.TryGetValue (CurrentFrictionEvent, out soundData))
                {
                    var emitter = FrictionEffectEmotterRef.gameObject.AddComponent<StudioEventEmitter>();
                    emitter.EventReference = CurrentFrictionEvent;
                    emitter.PlayEvent = FrictionEffectEmotterRef.PlayEvent;
                    emitter.StopEvent = FrictionEffectEmotterRef.StopEvent;

                    soundData = new FrictionSoundData () { Emitter = emitter };
                    FrictionSounds.Add (CurrentFrictionEvent, soundData);
                }

                if (!soundData.Emitter.IsPlaying ())
                {
                    soundData.Emitter.Play ();
                }

                soundData.LastFrictionTime = Time.time;
            }
        }

        /// <summary>
        /// Search for the desired event based on the collision magnitude and the collision layer.
        /// </summary>
        /// <param name="layer">Collision layer.</param>
        /// <param name="collisionMagnitude">Collision magnitude.</param>
        /// <param name="magnitudeDivider">Divider to calculate collision volume.</param>
        EventReference GetEventForCollision (int layer, float collisionMagnitude, out float magnitudeDivider)
        {
            for (int i = 0; i < CollisionEvents.Count; i++)
            {
                if (CollisionEvents[i].CollisionMask.LayerInMask (layer) && collisionMagnitude >= CollisionEvents[i].MinMagnitudeCollision && collisionMagnitude < CollisionEvents[i].MaxMagnitudeCollision)
                {
                    if (CollisionEvents[i].MaxMagnitudeCollision == float.PositiveInfinity)
                    {
                        magnitudeDivider = DefaultMagnitudeDivider;
                    }
                    else
                    {
                        magnitudeDivider = CollisionEvents[i].MaxMagnitudeCollision;
                    }

                    return CollisionEvents[i].EventRef;
                }
            }

            magnitudeDivider = DefaultMagnitudeDivider;
            return DefaultCollisionEventRef;
        }

        /// <summary>
        /// Search for the desired event based on the friction magnitude and the collision layer.
        /// </summary>
        /// <param name="layer">Collision layer.</param>
        /// <param name="collisionMagnitude">Collision magnitude.</param>
        EventReference GetEventForFriction (int layer, float collisionMagnitude)
        {
            for (int i = 0; i < FrictionEvents.Count; i++)
            {
                if (FrictionEvents[i].CollisionMask.LayerInMask (layer) && collisionMagnitude >= FrictionEvents[i].MinMagnitudeCollision && collisionMagnitude < FrictionEvents[i].MaxMagnitudeCollision)
                {
                    return FrictionEvents[i].EventRef;
                }
            }

            return DefaultFrictionEventRef;
        }

        #endregion //Collisions

        [System.Serializable]
        public struct ColissionEvent
        {
            public EventReference EventRef;
            public LayerMask CollisionMask;
            public float MinMagnitudeCollision;
            public float MaxMagnitudeCollision;
        }

        public class FrictionSoundData
        {
            public StudioEventEmitter Emitter;
            public float LastFrictionTime;
        }

        public class WheelSoundData
        {
            public StudioEventEmitter Emitter;
            public float Slip;
            public int WheelsCount;
        }
    }
}
