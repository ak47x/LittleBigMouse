﻿using System.Collections.Generic;
using System.Windows;
using WindowsMonitors;
using LbmScreenConfig;
using NotifyChange;
using WinAPI;

namespace MonitorVcp
{
    public static class VcpExpendMonitor
    {
        private static readonly Dictionary<DisplayMonitor, VcpControl> AllVcp = new Dictionary<DisplayMonitor, VcpControl>();
        public static VcpControl Vcp(this DisplayMonitor monitor)
        {
            if (AllVcp.ContainsKey(monitor)) return AllVcp[monitor];

            VcpControl vcp = new VcpControl(monitor);
            AllVcp.Add(monitor, vcp);
            return vcp;
        }
    }
    public enum Component { Red, Green, Blue, Brightness, Contrast }
    public class VcpControl : Notifier
    {
        public DisplayMonitor Monitor { get; }

        public VcpControl(DisplayMonitor monitor)
        {
            Monitor = monitor;
            Brightness = new MonitorLevel(GetBrightness, SetBrightness);
            Contrast = new MonitorLevel(GetContrast, SetContrast);

            Gain = new MonitorRgbLevel(GetGain, SetGain);
            Drive = new MonitorRgbLevel(GetDrive, SetDrive);
        }

        public bool AlternatePower => Monitor.ManufacturerCode == "DEL";

        public bool Power
        {
            get { return GetProperty<bool>(); }
            set
            {
                if (!SetProperty(value)) return;

                if (value)
                {
                    if(AlternatePower)
                        NativeMethods.SetVCPFeature(Monitor.HPhysical, 0xE1, 0);
                    else
                        NativeMethods.SetVCPFeature(Monitor.HPhysical, 0xD6, 1);

                }
                else
                {
                    if(AlternatePower)
                        NativeMethods.SetVCPFeature(Monitor.HPhysical, 0xE1, 1);
                    else
                        NativeMethods.SetVCPFeature(Monitor.HPhysical, 0xD6, 4);
                }
            }
        }


        public static DependencyProperty BrightnessProperty;
        public MonitorLevel Brightness { get; }
        public MonitorLevel Contrast { get; }

        public MonitorRgbLevel Gain { get; }
        public MonitorRgbLevel Drive { get; }

        private bool GetBrightness(ref uint min, ref uint value, ref uint max, uint component = 0)
        {
            return NativeMethods.GetMonitorBrightness(Monitor.HPhysical, ref min, ref value, ref max);
        }
        private bool SetBrightness(uint value, uint component = 0)
        {
            return NativeMethods.SetMonitorBrightness(Monitor.HPhysical, value);
        }
        private bool GetContrast(ref uint min, ref uint value, ref uint max, uint component = 0)
        {
            return NativeMethods.GetMonitorContrast(Monitor.HPhysical, ref min, ref value, ref max);
        }
        private bool SetContrast(uint value, uint component = 0)
        {
            return NativeMethods.SetMonitorContrast(Monitor.HPhysical, value);
        }
        private bool GetGain(ref uint min, ref uint value, ref uint max, uint component)
        {
            return NativeMethods.GetMonitorRedGreenOrBlueGain(Monitor.HPhysical, component, ref min, ref value, ref max);
        }
        private bool SetGain(uint value, uint component)
        {
            return NativeMethods.SetMonitorRedGreenOrBlueGain(Monitor.HPhysical, component, value);
        }
        private bool GetDrive(ref uint min, ref uint value, ref uint max, uint component)
        {
            return NativeMethods.GetMonitorRedGreenOrBlueDrive(Monitor.HPhysical, component, ref min, ref value, ref max);
        }
        private bool SetDrive(uint component, uint value)
        {
            return NativeMethods.SetMonitorRedGreenOrBlueDrive(Monitor.HPhysical, component, value);
        }
    }

    public delegate bool VcpGetter(ref uint min, ref uint value, ref uint max, uint component = 0);
    public delegate bool VcpSetter(uint value, uint component = 0);

    public class MonitorRgbLevel : Notifier
    {
        private readonly MonitorLevel[] _values = new MonitorLevel[3];

        public MonitorRgbLevel(VcpGetter getter, VcpSetter setter)
        {
            for (uint i = 0; i < 3; i++)
                _values[i] = new MonitorLevel(getter, setter, i);

        }
        public MonitorLevel Channel(uint channel) { return _values[channel]; }

        public MonitorLevel Red => Channel(0);
        public MonitorLevel Green => Channel(1);
        public MonitorLevel Blue => Channel(2);
    }

    public class MonitorLevel : Notifier
    {

        private readonly uint _component = 0;

        private readonly VcpSetter _componentSetter = null;
        private readonly VcpGetter _componentGetter = null;

        public MonitorLevel(VcpGetter getter, VcpSetter setter, uint component = 0)
        {
            _component = component;
            _componentSetter = setter;
            _componentGetter = getter;

            _threadSetter = new LossyThread( );

            _threadSetter.Add(GetValue);
        }

        private static readonly object LockDdcCi = new object();

        public void SetToMax() { CheckedValue = Max; }

        public void SetToMin() { CheckedValue = Min; }

        private void GetValue()
        {
            if (_componentGetter == null) return;

            uint min = 0;
            uint max = 0;
            uint value = 0;

            bool result = false;
            int tries = 10;

            lock (LockDdcCi) 
                while(!result && tries-- > 0)
                    result = _componentGetter.Invoke(ref min, ref value, ref max, _component);

            if (result)
            {
                Min = min;
                Max = max;
                SetProperty(value, "Value");               
            }
        }

        private readonly object _lockValue = new object();
        private readonly LossyThread _threadSetter;

        public uint Value
        {
            get { return GetProperty<uint>(); }
            set
            {
                //lock(_lockValue)
                if (SetProperty(value))
                {

                    bool result = false;
                    int tries = 10;

                    lock (LockDdcCi)
                            while (!result && tries-- > 0)
                            result = _componentSetter.Invoke(value, _component);
                        //SetProperty(ref _valueAsync, value, "ValueAsync"); 
                }
            }
        }

        [DependsOn("Value")]
        public uint CheckedValue
        {
            get { return Value;}
            set
            {
                int tries = 10;
                while (Value != value && tries-- > 0)
                {
                    Value = value;
                    GetValue();
                }
            }
        }

        [DependsOn("Value")]
        public uint ValueAsync
        {
            get { return GetProperty<uint>(); }
            set
            {
                //lock (_lockValue) //causes deadlock
                {
                    SetProperty(value);
                    _threadSetter.Add(() =>
                    {
                        CheckedValue = value;
                    });
                }
            }
        }
        public uint Min
        {
            get { return GetProperty<uint>(); }
            private set { SetProperty(value); }
        }

        public uint Max
        {
            get { return GetProperty<uint>(); }
            private set { SetProperty(value); }
        }
    }
}
