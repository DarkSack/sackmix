using SackMix.Core;

namespace SackMix.Tests;

public class ObsMonitorTests
{
    [Fact]
    public void ReplacesTheKeyInItsSectionOnly()
    {
        var ini = "[Video]\r\nMonitoringDeviceId=x\r\n\r\n[Audio]\r\nSampleRate=48000\r\nMonitoringDeviceId=default\r\n\r\n[Output]\r\nMode=Simple\r\n";
        var r = ObsMonitor.With(ini, "Audio", "MonitoringDeviceId", "{0.0.0.00000000}.{abc}");
        Assert.Equal("{0.0.0.00000000}.{abc}", ObsMonitor.Get(r, "Audio", "MonitoringDeviceId"));
        Assert.Equal("x", ObsMonitor.Get(r, "Video", "MonitoringDeviceId"));
        Assert.Equal("Simple", ObsMonitor.Get(r, "Output", "Mode"));
        Assert.Contains("\r\n", r);
    }

    [Fact]
    public void AddsAMissingKeyOrSection()
    {
        var r = ObsMonitor.With("[Audio]\nSampleRate=48000\n\n[Output]\nMode=Simple\n", "Audio", "MonitoringDeviceName", "Speakers (USP-03)");
        Assert.Equal("[Audio]\nSampleRate=48000\nMonitoringDeviceName=Speakers (USP-03)\n\n[Output]\nMode=Simple\n", r);

        var added = ObsMonitor.With("[Output]\nMode=Simple\n", "Audio", "MonitoringDeviceId", "default");
        Assert.Equal("default", ObsMonitor.Get(added, "Audio", "MonitoringDeviceId"));
        Assert.Equal("Simple", ObsMonitor.Get(added, "Output", "Mode"));
    }

    [Fact]
    public void FindsTheCurrentProfile()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "basic", "profiles", "Mio"));
            File.WriteAllText(Path.Combine(root, "user.ini"), "[Basic]\nProfile=Mío\nProfileDir=Mio\n");
            var basic = Path.Combine(root, "basic", "profiles", "Mio", "basic.ini");
            File.WriteAllText(basic, "[General]\nName=Mío\n");
            Assert.Equal(basic, ObsMonitor.ProfileFile(root));
            Assert.Equal("default", ObsMonitor.Current(basic));
            ObsMonitor.Set(basic, "{id}", "Audífonos");
            Assert.Equal("{id}", ObsMonitor.Current(basic));
            Assert.Equal("Audífonos", ObsMonitor.Get(File.ReadAllText(basic), "Audio", "MonitoringDeviceName"));
        }
        finally { Directory.Delete(root, true); }
    }
}
