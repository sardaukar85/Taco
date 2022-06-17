using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace PG
{
    // One of the most important parts of the component, it is responsible for management and management assistance.
    public partial class CarController :VehicleController
    {
        public SteerConfig Steer;

        protected float PrevSteerAngle;
        protected float CurrentSteerAngle;
        protected float WheelMaxSteerAngle;
        protected Wheel[] SteeringWheels;
        protected float HorizontalControl { get { return CarControl != null && !BlockControl? CarControl.Horizontal : 0; } }
        public bool ABSIsActive { get; private set; }

        void AwakeSteering ()
        {
            var steeringWheels = new List<Wheel>();
            foreach (var wheel in Wheels)
            {
                if (wheel.IsSteeringWheel)
                {
                    steeringWheels.Add (wheel);
                    if (wheel.SteerPercent.Abs() > WheelMaxSteerAngle)
                    {
                        WheelMaxSteerAngle = wheel.SteerPercent.Abs();
                    }
                }
            }
            SteeringWheels = steeringWheels.ToArray ();
        }

        /// <summary>
        /// Update all helpers logic.
        /// </summary>
        void FixedUpdateSteering ()
        {
            var needHelp = VelocityAngle.Abs() > 0.001f && VelocityAngle.Abs() < Steer.MaxVelocityAngleForHelp && CurrentSpeed > Steer.MinSpeedForHelp && CurrentGear > 0;
            float helpAngle = 0;
            var angularVelocity = RB.angularVelocity;

            if (needHelp)
            {
                for (int i = 0; i < SteeringWheels.Length; i++)
                {
                    if (Wheels[i].IsGrounded)
                    {
                        HelpAngularVelocity ();
                        break;
                    }
                }

                helpAngle = Mathf.Clamp (VelocityAngle * Steer.HelpDriftIntensity, -Steer.MaxSteerAngle, Steer.MaxSteerAngle);
            }
            else if (CurrentSpeed < Steer.MinSpeedForHelp && CurrentAcceleration > 0 && CurrentBrake > 0)
            {
                angularVelocity.y = Steer.HandBrakeAngularHelpCurve.Evaluate (angularVelocity.y.Abs()) * HorizontalControl * 5;
                RB.angularVelocity = angularVelocity;
            }

            float helpWhenChangeAngle = VehicleDirection == 1? (VelocityAngle - PrevVelocityAngle) * (Steer.MaxSteerAngle / 90): 0;

            var steerMultiplayer = Steer.EnableSteerLimit && VehicleDirection > 0? Steer.SteerLimitCurve.Evaluate (CurrentSpeed): 1;

            float targetSteerAngle = HorizontalControl * Steer.MaxSteerAngle * steerMultiplayer;

            //Wheel turn limitation.
            var targetAngle = Mathf.Clamp (helpAngle + targetSteerAngle, -Steer.MaxSteerAngle, Steer.MaxSteerAngle);

            //Calculation of the steering speed. The steering wheel should turn faster towards the velocity angle.
            //More details (With images) are described in the documentation.
            float steerAngleChangeSpeed;

            float currentAngleDiff = (VelocityAngle - CurrentSteerAngle).Abs();

            if (!needHelp || PrevSteerAngle > CurrentSteerAngle && CurrentSteerAngle > VelocityAngle || PrevSteerAngle < CurrentSteerAngle && CurrentSteerAngle < VelocityAngle)
            {
                steerAngleChangeSpeed = Steer.SteerChangeSpeedToVelocity.Evaluate (currentAngleDiff);
            }
            else
            {
                steerAngleChangeSpeed = Steer.SteerChangeSpeedFromVelocity.Evaluate (currentAngleDiff);
            }

            PrevSteerAngle = CurrentSteerAngle;
            CurrentSteerAngle = Mathf.MoveTowards (CurrentSteerAngle, targetAngle, steerAngleChangeSpeed * steerMultiplayer * Time.fixedDeltaTime);
            CurrentSteerAngle = (CurrentSteerAngle + helpWhenChangeAngle).Clamp (-Steer.MaxSteerAngle, Steer.MaxSteerAngle);

            //Apply a turn to the front wheels.
            for (int i = 0; i < SteeringWheels.Length; i++)
            {
                SteeringWheels[i].SetSteerAngle (CurrentSteerAngle);
            }
        }

        /// <summary>
        /// A method to help resistance drift and rotate the car with the handbrake.
        /// </summary>
        void HelpAngularVelocity ()
        {
            var angularVelocity = RB.angularVelocity;

            float angularHelp = 0;
            float angularHelpMultiplier = HorizontalControl * (CurrentSpeed / Steer.MaxSpeedForMaxAngularHelp).Clamp();


            if (HorizontalControl * VelocityAngle >= 0 && VelocityAngle.Abs() < 150)
            {
                //Drift resistance help.
                angularHelp = Steer.DriftResistanceCurve.Evaluate (VelocityAngle < 0 ? RB.angularVelocity.y : -RB.angularVelocity.y) * (VelocityAngle.Abs() / 60).Clamp() * angularHelpMultiplier;
            }

            if (InHandBrake)
            {
                //Handbrake resistance help.
                angularHelp += Steer.HandBrakeAngularHelpCurve.Evaluate (angularVelocity.y * -Mathf.Sign(angularVelocity.y)) * angularHelpMultiplier;
            }

            angularVelocity.y += angularHelp;
            RB.angularVelocity = angularVelocity;
        }

        /// <summary>
        /// Braking logic.
        /// </summary>
        void FixedUpdateBrakeLogic ()
        {
            ABSIsActive = false;
            //HandBrake
            if (InHandBrake)
            {
                for (int i = 0; i < Wheels.Length; i++)
                {
                    Wheels[i].SetHandBrake (true);
                }
            }
            //Brake and acceleration pressed at the same time for burnout.
            else if (CurrentAcceleration > 0 && CurrentBrake > 0 && CurrentSpeed < 5)
            {
                for (int i = 0; i < Wheels.Length; i++)
                {
                    Wheels[i].SetBrakeTorque (Wheels[i].DriveWheel ? 0 : CurrentBrake);
                }
            }
            //Just braking.
            else
            {
                if (Steer.ABS > 0 && CurrentBrake > 0)
                {
                    //ABS Logic.
                    float maxSlipForEnableAbs = 2.8f - Steer.ABS * 1.2f;
                    for (int i = 0; i < Wheels.Length; i++)
                    {
                        if (Wheels[i].ForwardSlipNormalized > maxSlipForEnableAbs)
                        {
                            Wheels[i].SetBrakeTorque (0);
                            ABSIsActive |= true;
                        }
                        else
                        {
                            Wheels[i].SetBrakeTorque (CurrentBrake);
                        }
                    }
                }
                else
                {
                    //Without ABS Logic.
                    for (int i = 0; i < Wheels.Length; i++)
                    {
                        Wheels[i].SetBrakeTorque (CurrentBrake);
                    }
                }
            }
        }

        [System.Serializable]
        public class SteerConfig
        {
            [Header("Steer settings")]
            public float MaxSteerAngle = 25;
            public bool EnableSteerLimit = true;                    //Enables limiting wheel turning based on car speed.
            public AnimationCurve SteerLimitCurve;                  //Limiting wheel turning if the EnableSteerLimit flag is enabled
            public AnimationCurve SteerChangeSpeedToVelocity;       //The speed of turn of the wheel in the direction of the velocity of the car.
            public AnimationCurve SteerChangeSpeedFromVelocity;     //The speed of turn of the wheel from the direction of the velocity of the car.

            [Header("Steer assistance")]
            public float MaxVelocityAngleForHelp = 120;             //The maximum degree of angle of the car relative to the velocity, at which the steering assistance will be provided.
            public float MinSpeedForHelp = 1.5f;

            [Space(10)]
            [Range(0, 1)] public float HelpDriftIntensity = 0.8f;   //The intensity of the automatic steering while drifting.

            [Header("Angular help")]
            public AnimationCurve HandBrakeAngularHelpCurve;        //The power of assistance that turns the car with the hand brake.
            public AnimationCurve DriftResistanceCurve;
            public float MaxSpeedForMaxAngularHelp = 20;

            [Header("Electronic assistants")]
            [Range(0, 1)]
            public float ABS;               //ABS to prevent wheel lock when braking.
            //[Range(0, 1)]
            //public float ESP;             TODO add ESP logic
            [Range(0, 1)]
            public float TCS;               //TCS to prevent wheel slip when accelerating.
        }
    }
}
