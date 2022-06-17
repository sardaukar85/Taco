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
    public class CarSFX :VehicleSFX
    {
        [Header("CarSFX")]

#pragma warning disable 0649

        [SerializeField] StudioEventEmitter EngineEmitter;
        [SerializeField] float MinTimeBetweenBlowOffSounds = 1;

#pragma warning restore 0649

        //PARAMETER_ID to not use a strings when calling "SetParameter" methods.
        FMOD.Studio.PARAMETER_ID RPMID;
        FMOD.Studio.PARAMETER_ID LoadID;
        FMOD.Studio.PARAMETER_ID TurboID;
        FMOD.Studio.PARAMETER_ID TurboBlowOffID;
        FMOD.Studio.PARAMETER_ID BackFireID;
        FMOD.Studio.PARAMETER_ID Boost;

        CarController Car;
        float LastBlowOffTime;

        protected override void Start ()
        {
            base.Start ();

            Car = Vehicle as CarController;

            if (Car == null)
            {
                Debug.LogErrorFormat ("[{0}] CarSFX without CarController in parent", name);
                enabled = false;
                return;
            }

            //Get PARAMETER_ID for all the necessary events.
            FMOD.Studio.PARAMETER_DESCRIPTION paramDescription;

            EngineEmitter.EventDescription.getParameterDescriptionByName ("RPM", out paramDescription);
            RPMID = paramDescription.id;

            EngineEmitter.EventDescription.getParameterDescriptionByName ("Load", out paramDescription);
            LoadID = paramDescription.id;

            EngineEmitter.EventDescription.getParameterDescriptionByName ("Turbo", out paramDescription);
            TurboID = paramDescription.id;

            EngineEmitter.EventDescription.getParameterDescriptionByName ("TurboBlowOff", out paramDescription);
            TurboBlowOffID = paramDescription.id;

            EngineEmitter.EventDescription.getParameterDescriptionByName ("BackFire", out paramDescription);
            BackFireID = paramDescription.id;

            EngineEmitter.EventDescription.getParameterDescriptionByName ("Boost", out paramDescription);
            Boost = paramDescription.id;

            EngineEmitter.SetParameter (RPMID, Car.MinRPM);
            EngineEmitter.SetParameter (LoadID, 1);

            if (BackFireID.data1 != 0 || BackFireID.data2 != 0)
            {
                Car.BackFireAction += OnBackFire;
            }

            UpdateAction += UpdateEngine;

            if (Car.Engine.EnableTurbo)
            {
                if (TurboID.data1 == 0 && TurboID.data2 == 0)
                {
                    Debug.LogErrorFormat ("EngineEmitter has no parameter 'Turbo' for Car: {0}", Car.name);
                }
                else
                {
                    UpdateAction += UpdateTurbo;
                }
            }

            if (Car.Engine.EnableBoost)
            {
                if (Boost.data1 == 0 && Boost.data2 == 0)
                {
                    Debug.LogErrorFormat ("EngineEmitter has no parameter 'Boost' for Car: {0}", Car.name);
                }
                else
                {
                    UpdateAction += UpdateBoost;
                }
            }
        }


        protected override void Update ()
        {
            base.Update ();
        }

        //Base engine sounds
        void UpdateEngine ()
        {
            EngineEmitter.SetParameter (RPMID, Car.EngineRPM);
            EngineEmitter.SetParameter (LoadID, Car.EngineLoad.Clamp (-1, 1));
        }

        //Additional turbo sound
        void UpdateTurbo ()
        {
            EngineEmitter.SetParameter (TurboID, Car.CurrentTurbo);
            if (Car.CurrentTurbo > 0.2f && (Car.CurrentAcceleration < 0.2f || Car.InChangeGear) && ((Time.realtimeSinceStartup - LastBlowOffTime) > MinTimeBetweenBlowOffSounds))
            {
                EngineEmitter.SetParameter (TurboBlowOffID, 0);
                EngineEmitter.SetParameter (TurboBlowOffID, Car.CurrentTurbo);
                LastBlowOffTime = Time.realtimeSinceStartup;
            }
        }

        //Additional boost sound
        void UpdateBoost ()
        {
            EngineEmitter.SetParameter (Boost, Car.InBoost ? 1 : 0);
        }

        void OnBackFire ()
        {
            EngineEmitter.SetParameter (BackFireID, Random.Range (0.1f, 1f));
            EngineEmitter.SetParameter (BackFireID, 0);
        }
    }
}
