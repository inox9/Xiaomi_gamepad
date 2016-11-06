﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HidLibrary;
using ScpDriverInterface;
using System.Threading;
using System.Runtime.InteropServices;

namespace mi
{
    public class Xiaomi_gamepad
    {
        private byte[] Vibration = { 0x20, 0x00, 0x00 };
        private Mutex rumble_mutex = new Mutex();

        public Xiaomi_gamepad(HidDevice Device, ScpBus scpBus, int index)
        {
            Device.WriteFeatureData(Vibration);

            Thread rThread = new Thread(() => rumble_thread(Device));
            // rThread.Priority = ThreadPriority.BelowNormal; 
            rThread.Start();

            Thread iThread = new Thread(() => input_thread(Device, scpBus, index));
            iThread.Priority = ThreadPriority.Highest;
            iThread.Start();
        }

        private void rumble_thread(HidDevice Device)
        {
            byte[] local_vibration = { 0x20, 0x00, 0x00 };
            while (true)
            {
                rumble_mutex.WaitOne();
                if (local_vibration[2] != Vibration[2] || Vibration[1] != local_vibration[1])
                {
                    local_vibration[2] = Vibration[2];
                    local_vibration[1] = Vibration[1];
                    rumble_mutex.ReleaseMutex();
                    Device.WriteFeatureData(local_vibration);
                    //Console.WriteLine("Big Motor: {0}, Small Motor: {1}", Vibration[2], Vibration[1]);
                }
                else
                {
                    rumble_mutex.ReleaseMutex();
                }
                Thread.Sleep(20);
            }
        }

        private short get_stick_value(byte raw_value, bool x)
        {
            short val = (short)((Math.Max(-127.0, raw_value - 128) / 127) * (x ? 32767 : -32767));
            if (raw_value != (x ? 0 : 255))
            {
                val -= 1;
            }
            return val;
        }

        private void input_thread(HidDevice Device, ScpBus scpBus, int index)
        {
            scpBus.PlugIn(index);
            X360Controller controller = new X360Controller();
            int timeout = 30;
            long last_changed = 0;
            long last_mi_button = 0;
            while (true)
            {
                HidDeviceData data = Device.Read(timeout);
                var currentState = data.Data;
                bool changed = false;
                if (data.Status == HidDeviceData.ReadStatus.Success && currentState.Length >= 21 && currentState[0] == 4)
                {
                    //Console.WriteLine(Program.ByteArrayToHexString(currentState));
                    X360Buttons Buttons = X360Buttons.None;
                    if ((currentState[1] & 1) != 0) Buttons |= X360Buttons.A;
                    if ((currentState[1] & 2) != 0) Buttons |= X360Buttons.B;
                    if ((currentState[1] & 8) != 0) Buttons |= X360Buttons.X;
                    if ((currentState[1] & 16) != 0) Buttons |= X360Buttons.Y;
                    if ((currentState[1] & 64) != 0) Buttons |= X360Buttons.LeftBumper;
                    if ((currentState[1] & 128) != 0) Buttons |= X360Buttons.RightBumper;

                    if ((currentState[2] & 32) != 0) Buttons |= X360Buttons.LeftStick;
                    if ((currentState[2] & 64) != 0) Buttons |= X360Buttons.RightStick;

                    if (currentState[4] != 15)
                    {
                        if (currentState[4] == 0 || currentState[4] == 1 || currentState[4] == 7) Buttons |= X360Buttons.Up;
                        if (currentState[4] == 4 || currentState[4] == 3 || currentState[4] == 5) Buttons |= X360Buttons.Down;
                        if (currentState[4] == 6 || currentState[4] == 5 || currentState[4] == 7) Buttons |= X360Buttons.Left;
                        if (currentState[4] == 2 || currentState[4] == 1 || currentState[4] == 3) Buttons |= X360Buttons.Right;
                    }

                    if ((currentState[2] & 8) != 0) Buttons |= X360Buttons.Start;
                    if ((currentState[2] & 4) != 0) Buttons |= X360Buttons.Back;



                    if ((currentState[20] & 1) != 0)
                    {
                        last_mi_button = (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
                        Buttons |= X360Buttons.Logo;
                    }
                    if (last_mi_button != 0) Buttons |= X360Buttons.Logo;


                    if (controller.Buttons != Buttons)
                    {
                        changed = true;
                        controller.Buttons = Buttons;
                    }

                    short LeftStickX = get_stick_value(currentState[5], true);
                    if (LeftStickX != controller.LeftStickX)
                    {
                        changed = true;
                        controller.LeftStickX = LeftStickX;
                    }

                    short LeftStickY = get_stick_value(currentState[6], false);
                    if (LeftStickY != controller.LeftStickY)
                    {
                        changed = true;
                        controller.LeftStickY = LeftStickY;
                    }

                    short RightStickX = get_stick_value(currentState[7], true);
                    if (RightStickX != controller.RightStickX)
                    {
                        changed = true;
                        controller.RightStickX = RightStickX;
                    }

                    short RightStickY = get_stick_value(currentState[8], false);
                    if (RightStickY != controller.RightStickY)
                    {
                        changed = true;
                        controller.RightStickY = RightStickY;
                    }

                    if (controller.LeftTrigger != currentState[11])
                    {
                        changed = true;
                        controller.LeftTrigger = currentState[11];
                    }

                    if (controller.RightTrigger != currentState[12])
                    {
                        changed = true;
                        controller.RightTrigger = currentState[12];

                    }
                }

                if (data.Status == HidDeviceData.ReadStatus.WaitTimedOut || (!changed && ((last_changed + timeout) < (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond))))
                {
                    changed = true;
                }

                if (changed)
                {
                    //Console.WriteLine("changed");
                    //Console.WriteLine((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond));
                    byte[] outputReport = new byte[8];
                    scpBus.Report(index, controller.GetReport(), outputReport);

                    if (outputReport[1] == 0x08)
                    {
                        byte bigMotor = outputReport[3];
                        byte smallMotor = outputReport[4];
                        rumble_mutex.WaitOne();
                        if (bigMotor != Vibration[2] || Vibration[1] != smallMotor)
                        {
                            Vibration[1] = smallMotor;
                            Vibration[2] = bigMotor;
                        }
                        rumble_mutex.ReleaseMutex();
                    }

                    if (last_mi_button != 0)
                    {
                        if ((last_mi_button + 100) < (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond))
                        {
                            last_mi_button = 0;
                            controller.Buttons ^= X360Buttons.Logo;
                        }
                    }

                    last_changed = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                }
            }
        }
    }

    class Program
    {
        private static ScpBus global_scpBus;
        static bool ConsoleEventCallback(int eventType)
        {
            if (eventType == 2)
            {
                global_scpBus.UnplugAll();
            }
            return false;
        }
        static ConsoleEventDelegate handler;   // Keeps it from getting garbage collected
                                               // Pinvoke
        private delegate bool ConsoleEventDelegate(int eventType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);



        public static string ByteArrayToHexString(byte[] bytes)
        {
            return string.Join(string.Empty, Array.ConvertAll(bytes, b => b.ToString("X2")));
        }



        static void Main(string[] args)
        {
            ScpBus scpBus = new ScpBus();
            scpBus.UnplugAll();
            global_scpBus = scpBus;

            handler = new ConsoleEventDelegate(ConsoleEventCallback);
            SetConsoleCtrlHandler(handler, true);

            Thread.Sleep(400);

            Xiaomi_gamepad[] gamepads = new Xiaomi_gamepad[4];
            int index = 1;
            string[] connectedDevices = new string[4];
            while (true)
            {
                var compatibleDevices = HidDevices.Enumerate(0x2717, 0x3144).ToList();
                foreach (var deviceInstance in compatibleDevices)
                {
                    if (index >= 5 || connectedDevices.Contains(deviceInstance.DevicePath))
                    {
                        continue;
                    }

                    HidDevice Device = deviceInstance;
                    try
                    {
                        Device.OpenDevice(DeviceMode.Overlapped, DeviceMode.Overlapped, ShareMode.Exclusive);
                    }
                    catch
                    {
                        Console.WriteLine("Could not open gamepad in exclusive mode. Try re-enable device.");
                        var instanceId = devicePathToInstanceId(deviceInstance.DevicePath);
                        if (TryReEnableDevice(instanceId))
                        {
                            try
                            {
                                Device.OpenDevice(DeviceMode.Overlapped, DeviceMode.Overlapped, ShareMode.Exclusive);
                                Console.WriteLine("Opened in exclusive mode.");
                            }
                            catch
                            {
                                Device.OpenDevice(DeviceMode.Overlapped, DeviceMode.Overlapped, ShareMode.ShareRead | ShareMode.ShareWrite);
                                Console.WriteLine("Opened in shared mode.");
                            }
                        }
                        else
                        {
                            Device.OpenDevice(DeviceMode.Overlapped, DeviceMode.Overlapped, ShareMode.ShareRead | ShareMode.ShareWrite);
                            Console.WriteLine("Opened in shared mode.");
                        }
                    }

                    byte[] Vibration = { 0x20, 0x00, 0x00 };
                    if (Device.WriteFeatureData(Vibration) == false)
                    {
                        Device.CloseDevice();
                        continue;
                    }

                    byte[] serialNumber;
                    byte[] product;
                    Device.ReadSerialNumber(out serialNumber);
                    Device.ReadProduct(out product);

                    gamepads[index - 1] = new Xiaomi_gamepad(Device, scpBus, index);
                    ++index;

                    Console.WriteLine(deviceInstance);
                    Console.WriteLine("Added new device.");
                    connectedDevices[index - 1] = deviceInstance.DevicePath;
                }

                Thread.Sleep(5000);
            }
        }

        private static bool TryReEnableDevice(string deviceInstanceId)
        {
            try
            {
                bool success;
                Guid hidGuid = new Guid();
                HidLibrary.NativeMethods.HidD_GetHidGuid(ref hidGuid);
                IntPtr deviceInfoSet = HidLibrary.NativeMethods.SetupDiGetClassDevs(ref hidGuid, deviceInstanceId, 0, HidLibrary.NativeMethods.DIGCF_PRESENT | HidLibrary.NativeMethods.DIGCF_DEVICEINTERFACE);
                HidLibrary.NativeMethods.SP_DEVINFO_DATA deviceInfoData = new HidLibrary.NativeMethods.SP_DEVINFO_DATA();
                deviceInfoData.cbSize = Marshal.SizeOf(deviceInfoData);
                success = HidLibrary.NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData);
                if (!success)
                {
                    Console.WriteLine("Error getting device info data, error code = " + Marshal.GetLastWin32Error());
                }
                success = HidLibrary.NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 1, ref deviceInfoData); // Checks that we have a unique device
                if (success)
                {
                    Console.WriteLine("Can't find unique device");
                }

                HidLibrary.NativeMethods.SP_PROPCHANGE_PARAMS propChangeParams = new HidLibrary.NativeMethods.SP_PROPCHANGE_PARAMS();
                propChangeParams.classInstallHeader.cbSize = Marshal.SizeOf(propChangeParams.classInstallHeader);
                propChangeParams.classInstallHeader.installFunction = HidLibrary.NativeMethods.DIF_PROPERTYCHANGE;
                propChangeParams.stateChange = HidLibrary.NativeMethods.DICS_DISABLE;
                propChangeParams.scope = HidLibrary.NativeMethods.DICS_FLAG_GLOBAL;
                propChangeParams.hwProfile = 0;
                success = HidLibrary.NativeMethods.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propChangeParams, Marshal.SizeOf(propChangeParams));
                if (!success)
                {
                    Console.WriteLine("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
                    return false;
                }
                success = HidLibrary.NativeMethods.SetupDiCallClassInstaller(HidLibrary.NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
                if (!success)
                {
                    Console.WriteLine("Error disabling device, error code = " + Marshal.GetLastWin32Error());
                    return false;

                }
                propChangeParams.stateChange = HidLibrary.NativeMethods.DICS_ENABLE;
                success = HidLibrary.NativeMethods.SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref propChangeParams, Marshal.SizeOf(propChangeParams));
                if (!success)
                {
                    Console.WriteLine("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
                    return false;
                }
                success = HidLibrary.NativeMethods.SetupDiCallClassInstaller(HidLibrary.NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
                if (!success)
                {
                    Console.WriteLine("Error enabling device, error code = " + Marshal.GetLastWin32Error());
                    return false;
                }

                HidLibrary.NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);

                return true;
            }
            catch
            {
                Console.WriteLine("Can't reenable device");
                return false;
            }
        }

        private static string devicePathToInstanceId(string devicePath)
        {
            string deviceInstanceId = devicePath;
            deviceInstanceId = deviceInstanceId.Remove(0, deviceInstanceId.LastIndexOf('\\') + 1);
            deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.LastIndexOf('{'));
            deviceInstanceId = deviceInstanceId.Replace('#', '\\');
            if (deviceInstanceId.EndsWith("\\"))
            {
                deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.Length - 1);
            }
            return deviceInstanceId;
        }
    }
}
