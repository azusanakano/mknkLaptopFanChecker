using System;
using Mknk.LaptopFanChecker;

internal static class CoolingCompatibilityTests
{
    public static void Run(Action<bool, string> assert)
    {
        SensorSnapshot snapshot = new SensorSnapshot();
        CoolingSensorSelection selector = new CoolingSensorSelection();
        selector.SelectFan(new[] { Sensor("CPU Pump", "Fan", 2200), Sensor("Fan #1", "Fan", 600), Sensor("Chassis Fan", "Fan", 2000) }, snapshot);
        assert(!snapshot.FanSensorAvailable, "pump and unidentified desktop fans cannot certify CPU fan");
        SensorRecord first = Sensor("CPU Fan", "Fan", 500);
        SensorRecord second = Sensor("CPU Optional Fan", "Fan", 1200);
        selector.SelectFan(new[] { first, second }, snapshot);
        assert(snapshot.FanSensorAvailable && snapshot.FanRpm == 500, "select explicit CPU fan");
        second.Value = 3000;
        snapshot = new SensorSnapshot();
        selector.SelectFan(new[] { second, first }, snapshot);
        assert(snapshot.FanRpm == 500, "CPU fan identity does not follow fastest RPM");
        snapshot = new SensorSnapshot();
        selector.SelectFan(new[] { second }, snapshot);
        assert(!snapshot.FanSensorAvailable, "sensor loss does not silently switch fans");
        snapshot = new SensorSnapshot();
        CoolingSensorSelection.SelectTemperature(new[] { Sensor("CPU Package", "Temperature", 60, "Cpu", "cpu0"), Sensor("Core Max", "Temperature", 105, "Cpu", "cpu1") }, snapshot);
        assert(snapshot.TemperatureC == 105, "hottest socket wins over temperature label priority");
        snapshot = new SensorSnapshot();
        CoolingSensorSelection.SelectTemperature(new[] { Sensor("CPU Core Distance to TjMax", "Temperature", 95, "Cpu") }, snapshot);
        assert(!snapshot.TemperatureC.HasValue, "distance to TjMax is not a temperature");
        snapshot = new SensorSnapshot();
        CoolingSensorSelection.SelectTemperature(new[] { Sensor("Core (Tctl)", "Temperature", 95, "Cpu"), Sensor("Core (Tdie)", "Temperature", 75, "Cpu") }, snapshot);
        assert(snapshot.TemperatureC == 75, "Tdie avoids control-temperature offset");
        snapshot = new SensorSnapshot();
        CoolingSensorSelection.SelectCpuLoad(new[] { Sensor("CPU Total", "Load", 0, "Cpu", "cpu0"), Sensor("CPU Total", "Load", 100, "Cpu", "cpu1") }, snapshot);
        assert(snapshot.CpuLoadPercent == 50, "load includes all CPU sockets");
    }

    private static SensorRecord Sensor(string name, string type, double value, string hardware = "SuperIO", string id = "board0")
    {
        return new SensorRecord { SensorName = name, SensorType = type, Value = value, HardwareType = hardware,
            HardwareIdentifier = id, Identifier = id + "/" + type + "/" + name, HardwareName = id, Path = id };
    }
}
