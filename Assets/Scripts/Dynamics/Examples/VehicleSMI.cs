/**
 * Copyright (C) 2016, Jaguar Land Rover
 * This program is licensed under the terms and conditions of the
 * Mozilla Public License, version 2.0.  The full text of the
 * Mozilla Public License is at https://www.mozilla.org/MPL/2.0/
 * 
 * Copyright (c) 2019-2021 LG Electronics, Inc.
 */

using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

public class VehicleSMI : MonoBehaviour, IVehicleDynamics
{
    private Rigidbody RB;
    public Vector3 Velocity => RB.velocity;
    public Vector3 AngularVelocity => RB.angularVelocity;

    public Transform BaseLink { get { return BaseLinkTransform; } }
    public Transform BaseLinkTransform;

    public float AccellInput { get; set; } = 0f;
    public float BrakeInput { get; set; } = 0f;
    public float SteerInput { get; set; } = 0f;

    public bool HandBrake { get; set; } = false;
    public float CurrentRPM { get; set; } = 0f;
    public float CurrentGear { get; set; } = 1f;
    public bool Reverse { get; set; } = false;

    public bool EmergencyStopped { get; set; } = false;

    public float WheelAngle
    {
        get
        {
            if (Axles != null && Axles.Count > 0 && Axles[0] != null)
            {
                return (Axles[0].Left.steerAngle + Axles[0].Right.steerAngle) * 0.5f;
            }
            return 0.0f;
        }
    }
    public float Speed
    {
        get
        {
            if (RB != null)
            {
                return RB.velocity.magnitude;
            }
            return 0f;
        }
    }
    public IgnitionStatus CurrentIgnitionStatus { get; set; } = IgnitionStatus.Off;

    public List<AxleInfo> Axles;
    public Vector3 CenterOfMass = new Vector3(0f, 0.35f, 0f);

    [Tooltip("torque at peak of torque curve")]
    public float MaxMotorTorque = 450f;

    [Tooltip("torque at max brake")]
    public float MaxBrakeTorque = 3000f;

    [Tooltip("steering range is +-maxSteeringAngle")]
    public float _MaxSteeringAngle = 39.4f;

    public float MaxSteeringAngle
    {
        get
        {
            return _MaxSteeringAngle;
        }
        set
        {
            _MaxSteeringAngle = System.Math.Abs(value);
        }
    }

    [Tooltip("idle rpm")]
    public float MinRPM = 800f;

    [Tooltip("max rpm")]
    public float MaxRPM = 8299f;

    [Tooltip("gearbox ratios")]
    public float[] GearRatios = new float[] { 4.17f, 3.14f, 2.11f, 1.67f, 1.28f, 1f, 0.84f, 0.67f };
    public float FinalDriveRatio = 2.56f;

    [Tooltip("min time between gear changes")]
    public float ShiftDelay = 0.7f;

    [Tooltip("time interpolated for gear shift")]
    public float ShiftTime = 0.4f;

    [Tooltip("torque curve that gives torque at specific percentage of max RPM")]
    public AnimationCurve RPMCurve;
    [Tooltip("curves controlling whether to shift up at specific rpm, based on throttle position")]
    public AnimationCurve ShiftUpCurve;
    [Tooltip("curves controlling whether to shift down at specific rpm, based on throttle position")]
    public AnimationCurve ShiftDownCurve;

    [Tooltip("Air Drag Coefficient")]
    public float AirDragCoeff = 1.0f;
    [Tooltip("Air Downforce Coefficient")]
    public float AirDownForceCoeff = 2.0f;
    [Tooltip("Tire Drag Coefficient")]
    public float TireDragCoeff = 4.0f;

    [Tooltip("wheel collider damping rate")]
    public float WheelDamping = 1f;

    [Tooltip("autosteer helps the car maintain its heading")]
    [Range(0, 1)]
    public float AutoSteerAmount = 0.338f;

    [Tooltip("traction control limits torque based on wheel slip - traction reduced by amount when slip exceeds the tractionControlSlipLimit")]
    [Range(0, 1)]
    public float TractionControlAmount = 0.675f;
    public float TractionControlSlipLimit = 0.8f;

    [Tooltip("how much to smooth out real RPM")]
    public float RPMSmoothness = 20f;

    private float CurrentTorque = 0f;

    private int NumberOfDrivingWheels;
    private float OldRotation = 0f;
    private float TractionControlAdjustedMaxTorque = 0f;

    private int TargetGear = 1;
    private int LastGear = 1;
    private float GearRatio = 0f;
    private bool Shifting = false;
    private float LastShift = 0.0f;

    private float WheelsRPM = 0f;
    private float MileTicker = 0f;

    private IAgentController Controller;


    private void tryOverrideVariables()
    {
        var configFile = System.Environment.CurrentDirectory+"/vehicle-dynamics-config.txt";

        if (File.Exists(@configFile))
        {
            //read vehicle params from file
            string vehichleDynamicsContet = System.IO.File.ReadAllText(@configFile);
            
            Debug.Log($"Override vehicle dynamics variables from: {@configFile}");
            string[] lines = vehichleDynamicsContet.Split('\n');
            foreach (string line in lines)
            {
                if (!line.Contains("//") && line.Trim().Length>0)
                {
                    var entry = line.Split('=');
                    float value = float.Parse((entry[1].Split('f')[0].Trim()));
                    switch (entry[0].Trim()){
                        case "RB.mass":
                            RB.mass = value;
                            break;
                        case "RB.drag":
                            RB.drag = value;
                            break;
                        case "RB.angularDrag":
                            RB.angularDrag = value;
                            break;
                        case "MaxMotorTorque":
                            MaxMotorTorque = value;
                            break;
                        case "MaxBrakeTorque":
                            MaxBrakeTorque = value;
                            break;
                        case "MaxSteeringAngle":
                            MaxSteeringAngle = value;
                            break;
                        case "MinRPM":
                            MinRPM = value;
                            break;
                        case "MaxRPM":
                            MaxRPM = value;
                            break;
                        case "GearRatios.1":
                            if (GearRatios.Length>=1){ GearRatios[0] = value; }
                            break;
                        case "GearRatios.2":
                            if (GearRatios.Length>=2){ GearRatios[1] = value; }
                            break;
                        case "GearRatios.3":
                            if (GearRatios.Length>=3){ GearRatios[2] = value; }
                            break;
                        case "GearRatios.4":
                            if (GearRatios.Length>=4){ GearRatios[3] = value; }
                            break;
                        case "GearRatios.5":
                            if (GearRatios.Length>=5){ GearRatios[4] = value; }
                            break;
                        case "GearRatios.6":
                            if (GearRatios.Length>=6){ GearRatios[5] = value; }
                            break;
                        case "GearRatios.7":
                            if (GearRatios.Length>=7){ GearRatios[6] = value; }
                            break;
                        case "GearRatios.8":
                            if (GearRatios.Length>=8){ GearRatios[7] = value; }
                            break;
                        case "GearRatios.9":
                            if (GearRatios.Length>=9){ GearRatios[8] = value; }
                            break;
                        case "GearRatios.10":
                            if (GearRatios.Length>=10){ GearRatios[9] = value; }
                            break;
                        case "FinalDriveRatio":
                            FinalDriveRatio = value;
                            break;
                        case "ShiftDelay":
                            ShiftDelay = value;
                            break;
                        case "ShiftTime":
                            ShiftTime = value;
                            break;
                        case "AirDragCoeff":
                            AirDragCoeff = value;
                            break;
                        case "AirDownForceCoeff":
                            AirDownForceCoeff = value;
                            break;
                        case "TireDragCoeff":
                            TireDragCoeff = value;
                            break;
                        case "WheelDamping":
                            WheelDamping = value;
                            break;
                        case "AutoSteerAmount":
                            AutoSteerAmount = value;
                            break;
                        case "TractionControlAmount":
                            TractionControlAmount = value;
                            break;
                        case "TractionControlSlipLimit":
                            TractionControlSlipLimit = value;
                            break;
                        case "RPMSmoothness":
                            RPMSmoothness = value;
                            break;
                        default:
                            break;
                    }
                }
            }
        }
    }

    public void Awake()
    {
        RB = GetComponent<Rigidbody>();
        Controller = GetComponent<IAgentController>();

        RB.centerOfMass = CenterOfMass;
        NumberOfDrivingWheels = Axles.Where(a => a.Motor).Count() * 2;
        TractionControlAdjustedMaxTorque = MaxMotorTorque - (TractionControlAmount * MaxMotorTorque);
        foreach (var axle in Axles)
        {
            axle.Left.ConfigureVehicleSubsteps(5f, 30, 10);
            axle.Right.ConfigureVehicleSubsteps(5f, 30, 10);
            axle.Left.wheelDampingRate = WheelDamping;
            axle.Right.wheelDampingRate = WheelDamping;
        }

        tryOverrideVariables();//set custom config
    }

    private void Update()
    {
        UpdateWheelVisuals();
    }

    public void FixedUpdate()
    {
        if (EmergencyStopped)
        {
            HandBrake = true;
            CurrentIgnitionStatus = IgnitionStatus.Off;
        } else {
            GetInput();
        }
        RB.AddForce(-AirDragCoeff * RB.velocity * RB.velocity.magnitude); //air drag (quadratic)
        RB.AddForce(-AirDownForceCoeff * RB.velocity.sqrMagnitude * transform.up); //downforce (quadratic)
        RB.AddForceAtPosition(-TireDragCoeff * RB.velocity, transform.position); //tire drag (Linear)

        SetGearRatio();
        SetRPM();
        ApplySteer();
        ApplyTorque();
        TractionControl();
        SetMileTick();
    }

    public bool GearboxShiftUp()
    {
        if (Reverse)
        {
            Reverse = false;
        }
        else
        {
            LastGear = Mathf.RoundToInt(CurrentGear);
            TargetGear = LastGear + 1;
            LastShift = Time.time;
            Shifting = true;
        }
        return true;
    }

    public bool GearboxShiftDown()
    {
        if (Mathf.RoundToInt(CurrentGear) == 1)
        {
            Reverse = true;
        }
        else
        {
            LastGear = Mathf.RoundToInt(CurrentGear);
            TargetGear = LastGear - 1;
            LastShift = Time.time;
            Shifting = true;
        }
        return true;
    }

    public bool ShiftFirstGear()
    {
        if (Reverse != false)
        {
            LastGear = 1;
            TargetGear = 1;
            LastShift = Time.time;
            Reverse = false;
        }
        return true;
    }

    public bool ShiftReverse()
    {
        LastGear = 1;
        TargetGear = 1;
        LastShift = Time.time;
        Reverse = true;
        return true;
    }

    public bool ToggleReverse()
    {
        if (Reverse)
        {
            ShiftFirstGear();
        }
        else
        {
            ShiftReverse();
        }
        return true;
    }

    public bool ShiftReverseAutoGearBox()
    {
        if (Time.time - LastShift > ShiftDelay)
        {
            if (CurrentRPM / MaxRPM < ShiftDownCurve.Evaluate(AccellInput) && Mathf.RoundToInt(CurrentGear) > 1)
            {
                GearboxShiftDown();
            }
        }

        if (CurrentGear == 1)
        {
            Reverse = true;
        }
        return true;
    }

    public bool ToggleIgnition()
    {
        CurrentIgnitionStatus = CurrentIgnitionStatus == IgnitionStatus.On ? IgnitionStatus.Off : IgnitionStatus.On;
        return true;
    }

    public void StartEngine()
    {
        CurrentIgnitionStatus = IgnitionStatus.On;
    }

    public void StopEngine()
    {
        CurrentIgnitionStatus = IgnitionStatus.Off;
    }

    public bool ToggleHandBrake()
    {
        HandBrake = !HandBrake;
        return true;
    }

    public bool SetHandBrake(bool state)
    {
        HandBrake = state;
        return true;
    }

    public bool ForceReset(Vector3 pos, Quaternion rot)
    {
        RB.MovePosition(pos);
        RB.MoveRotation(rot);
        RB.velocity = Vector3.zero;
        RB.angularVelocity = Vector3.zero;
        CurrentGear = 1;
        CurrentRPM = 0f;
        AccellInput = 0f;
        BrakeInput = 0f;
        SteerInput = 0f;

        foreach (var axle in Axles)
        {
            axle.Left.brakeTorque = Mathf.Infinity;
            axle.Right.brakeTorque = Mathf.Infinity;
            axle.Left.motorTorque = 0f;
            axle.Right.motorTorque = 0f;
        }
        return true;
    }

    public float GetWheelAngularVelocity(int wheelIndex)
    {
        int whichAxle = wheelIndex/2;
        bool isLeft = wheelIndex%2 == 0;
        const float rpmToRadps = (float)(System.Math.PI * 2.0 / 60.0);

        if (isLeft)
        {
            return Axles[whichAxle].Left.rpm * rpmToRadps;
        } else {
            return Axles[whichAxle].Right.rpm * rpmToRadps;
        }
    }

    private void UpdateWheelVisuals()
    {
        foreach (var axle in Axles)
        {
            ApplyLocalPositionToVisuals(axle.Left, axle.LeftVisual);
            ApplyLocalPositionToVisuals(axle.Right, axle.RightVisual);
        }
    }

    private void ApplyLocalPositionToVisuals(WheelCollider collider, GameObject visual)
    {
        if (visual == null || collider == null)
        {
            return;
        }

        Vector3 position;
        Quaternion rotation;
        collider.GetWorldPose(out position, out rotation);

        visual.transform.position = position;
        visual.transform.rotation = rotation;
    }

    private void SetMileTick()
    {
        float deltaDistance = WheelsRPM / 60.0f * (Axles[1].Left.radius * 2.0f * Mathf.PI) * Time.fixedDeltaTime;
        MileTicker += deltaDistance;
        if ((MileTicker * 0.00062137f) > 1)
        {
            MileTicker = 0;
        }
    }

    private void SetGearRatio()
    {
        GearRatio = Mathf.Lerp(GearRatios[Mathf.FloorToInt(CurrentGear) - 1], GearRatios[Mathf.CeilToInt(CurrentGear) - 1], CurrentGear - Mathf.Floor(CurrentGear));
        if (Reverse)
        {
            GearRatio = -1.0f * GearRatios[0];
        }

        AutoGearBox();
    }

    private void AutoGearBox()
    {
        if (CurrentIgnitionStatus != IgnitionStatus.On)
        {
            return;
        }

        //check delay so we cant shift up/down too quick
        //FIXME lock gearbox for certain amount of time if user did override
        if (Time.time - LastShift > ShiftDelay)
        {
            //shift up
            if (CurrentRPM / MaxRPM > ShiftUpCurve.Evaluate(AccellInput) && Mathf.RoundToInt(CurrentGear) < GearRatios.Length)
            {
                //don't shift up if we are just spinning in 1st
                if (Mathf.RoundToInt(CurrentGear) > 1 || RB.velocity.magnitude > 15f)
                {
                    GearboxShiftUp();
                }
            }
            //else down
            if (CurrentRPM / MaxRPM < ShiftDownCurve.Evaluate(AccellInput) && Mathf.RoundToInt(CurrentGear) > 1)
            {
                GearboxShiftDown();
            }

        }

        if (Shifting)
        {
            float lerpVal = (Time.time - LastShift) / ShiftTime;
            CurrentGear = Mathf.Lerp(LastGear, TargetGear, lerpVal);
            if (lerpVal >= 1f)
                Shifting = false;
        }

        //clamp to gear range
        if (CurrentGear >= GearRatios.Length)
        {
            CurrentGear = GearRatios.Length - 1;
        }
        else if (CurrentGear < 1)
        {
            CurrentGear = 1;
        }
    }

    private void SetRPM()
    {
        //calc engine RPM from wheel rpm
        WheelsRPM = (Axles[1].Right.rpm + Axles[1].Left.rpm) / 2f;
        if (WheelsRPM < 0)
        {
            WheelsRPM = 0;
        }

        // if the engine is on, the fuel injectors are going to be triggered at minRPM
        // to keep the engine running.  If the engine is OFF, then the engine will eventually
        // go all the way down to 0, because there's nothing keeping it spinning.
        var minPossibleRPM = CurrentIgnitionStatus == IgnitionStatus.On ? MinRPM : 0.0f;
        CurrentRPM = Mathf.Lerp(CurrentRPM, minPossibleRPM + (WheelsRPM * FinalDriveRatio * GearRatio), Time.fixedDeltaTime * RPMSmoothness);
        if (CurrentRPM < 0.02f)
        {
            CurrentRPM = 0.0f;
        }
    }

    private void ApplySteer()
    {
        //convert inputs to torques
        float steer = MaxSteeringAngle * SteerInput;
        foreach (var axle in Axles)
        {
            if (axle.Steering)
            {
                axle.Left.steerAngle = steer;
                axle.Right.steerAngle = steer;
            }
        }

        AutoSteer();
    }

    private void AutoSteer()
    {
        if (CurrentIgnitionStatus != IgnitionStatus.On)
        {
            return;
        }

        foreach (var axle in Axles) //find out which wheels are on the ground
            {
            axle.GroundedLeft = axle.Left.GetGroundHit(out axle.HitLeft);
            axle.GroundedRight = axle.Right.GetGroundHit(out axle.HitRight);

            if (axle.GroundedLeft == false || axle.GroundedRight == false)
            {
                return; //bail if a wheel isn't on the ground
            }
        }

        var yawRate = OldRotation - transform.eulerAngles.y;
        if (Mathf.Abs(yawRate) < 10f) //don't adjust if the yaw rate is super high
        {
            RB.velocity = Quaternion.AngleAxis(yawRate * AutoSteerAmount, Vector3.up) * RB.velocity;
        }

        OldRotation = transform.eulerAngles.y;
    }

    private void ApplyTorque()
    {
        CurrentTorque = (float.IsNaN(CurrentRPM / MaxRPM)) ? 0.0f : RPMCurve.Evaluate(CurrentRPM / MaxRPM) * GearRatio * FinalDriveRatio * TractionControlAdjustedMaxTorque;

        // acceleration is ignored when engine is not running, brakes are available still.
        if (AccellInput >= 0)
        {
            //motor
            float torquePerWheel = CurrentIgnitionStatus == IgnitionStatus.On ? AccellInput * (CurrentTorque / NumberOfDrivingWheels) : 0f;
            //Debug.Log(torquePerWheel);
            foreach (var axle in Axles)
            {
                if (axle.Motor)
                {
                    axle.Left.motorTorque = torquePerWheel;
                    axle.Right.motorTorque = torquePerWheel;
                } else {
                    axle.Left.motorTorque = 0f;
                    axle.Right.motorTorque = 0f;
                }

                //axle.Left.brakeTorque = 0f;
                //axle.Right.brakeTorque = 0f;
            }
        }
        // TODO: to get brake + accelerator working at the same time, modify this area.
        // You'll need to do some work to separate the brake and accel pedal inputs, though.
        // TODO: handBrake should apply full braking to rear axle (possibly all axles), without
        // changing the accelInput
        //else
        if (BrakeInput >= 0)
        {
            //brakes
            foreach (var axle in Axles)
            {
                var brakeTorque = MaxBrakeTorque * BrakeInput * axle.BrakeBias;
                axle.Left.brakeTorque = brakeTorque;
                axle.Right.brakeTorque = brakeTorque;
                //axle.Left.motorTorque = 0f;
                //axle.Right.motorTorque = 0f;
            }
        }
    }

    private void TractionControl()
    {
        foreach (var axle in Axles)
        {
            if (axle.Motor)
            {
                if (axle.Left.isGrounded)
                    AdjustTractionControlTorque(axle.HitLeft.forwardSlip);

                if (axle.Right.isGrounded)
                    AdjustTractionControlTorque(axle.HitRight.forwardSlip);
            }
        }
    }

    private void AdjustTractionControlTorque(float forwardSlip)
    {
        if (forwardSlip >= TractionControlSlipLimit && TractionControlAdjustedMaxTorque >= 0)
        {
            TractionControlAdjustedMaxTorque -= 10 * TractionControlAmount;
            if (TractionControlAdjustedMaxTorque < 0)
                TractionControlAdjustedMaxTorque = 0f;
        }
        else
        {
            TractionControlAdjustedMaxTorque += 10 * TractionControlAmount;
            if (TractionControlAdjustedMaxTorque > MaxMotorTorque)
                TractionControlAdjustedMaxTorque = MaxMotorTorque;
        }
    }

    private void GetInput()
    {
        if (Controller != null)
        {
            SteerInput = Controller.SteerInput;
            AccellInput = Controller.AccelInput;
            BrakeInput = Controller.BrakeInput;
        }

        if (HandBrake)
        {
            BrakeInput = 1.0f;
        }
    }
}
