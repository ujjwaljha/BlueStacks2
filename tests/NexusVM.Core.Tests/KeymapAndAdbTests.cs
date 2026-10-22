using NexusVM.Core.Input;

namespace NexusVM.Core.Tests;

public sealed class KeymapAndAdbTests
{
    [Fact]
    public void Default_profile_parses_the_documented_bindings()
    {
        var profile = KeymapProfile.Parse(File.ReadAllText(Path.Combine(
            FindRepoRoot(), "assets", "keymaps", "default.json")));

        Assert.Equal("Standard FPS Profile", profile.ProfileName);
        var forward = Require(profile, "w");
        Assert.Equal("swipe", forward.Action);
        Assert.Equal(200, forward.StartX);
        Assert.Equal(700, forward.EndY);
        var fire = Require(profile, "LeftClick");
        Assert.Equal("tap", fire.Action);
        Assert.Equal(960, fire.TargetX);
        Assert.Equal(540, fire.TargetY);
    }

    [Fact]
    public void Tap_and_swipe_become_adb_input_arguments()
    {
        var profile = KeymapProfile.Parse("""
            {
              "profileName": "Test",
              "mappings": [
                { "key": "F", "action": "tap", "targetX": 10, "targetY": 20, "label": "Fire" },
                { "key": "W", "action": "swipe", "startX": 1, "startY": 2, "endX": 3, "endY": 4, "label": "Forward" }
              ]
            }
            """);

        var tap = Require(profile, "F");
        Assert.Equal(
            new[] { "-s", "127.0.0.1:5555", "shell", "input", "tap", "10", "20" },
            AdbInput.Command("127.0.0.1:5555", tap));

        var swipe = Require(profile, "W");
        Assert.Equal(
            new[] { "-s", "127.0.0.1:5555", "shell", "input", "swipe", "1", "2", "3", "4", "150" },
            AdbInput.Command("127.0.0.1:5555", swipe));
    }

    [Theory]
    [InlineData("10.0.0.8:5555")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:0")]
    [InlineData("emulator-5554")]
    public void Non_loopback_serials_are_rejected(string serial)
    {
        var profile = KeymapProfile.Parse("""
            { "profileName": "Test", "mappings": [ { "key": "F", "action": "tap", "targetX": 1, "targetY": 2 } ] }
            """);
        var tap = Require(profile, "F");
        Assert.Throws<ArgumentException>(() => AdbInput.Command(serial, tap));
    }

    [Fact]
    public void Keys_with_punctuation_are_rejected()
    {
        var exception = Assert.Throws<KeymapFormatException>(() => KeymapProfile.Parse("""
            { "profileName": "Test", "mappings": [ { "key": "W;reboot", "action": "tap", "targetX": 1, "targetY": 2 } ] }
            """));
        Assert.Contains("letters and digits", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinates_outside_the_guest_surface_are_rejected()
    {
        var exception = Assert.Throws<KeymapFormatException>(() => KeymapProfile.Parse("""
            { "profileName": "Test", "mappings": [ { "key": "F", "action": "tap", "targetX": -1, "targetY": 2 } ] }
            """));
        Assert.Contains("targetX", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_and_disconnect_are_loopback_only()
    {
        Assert.Equal(new[] { "connect", "127.0.0.1:5555" }, AdbInput.ConnectCommand("127.0.0.1:5555"));
        Assert.Equal(new[] { "disconnect", "127.0.0.1:5555" }, AdbInput.DisconnectCommand("127.0.0.1:5555"));
        Assert.Throws<ArgumentException>(() => AdbInput.EnsureSafe(new[] { "shell", "reboot" }));
        Assert.Throws<ArgumentException>(() => AdbInput.ConnectCommand("10.0.0.8:5555"));
    }

    [Fact]
    public void Saved_json_round_trips_tap_and_swipe()
    {
        var profile = KeymapProfile.Parse("""
            {
              "profileName": "Round",
              "mappings": [
                { "key": "F", "action": "tap", "targetX": 10, "targetY": 20, "label": "Fire" },
                { "key": "W", "action": "swipe", "startX": 1, "startY": 2, "endX": 3, "endY": 4, "label": "Forward" }
              ]
            }
            """);

        var again = KeymapProfile.Parse(profile.ToJson());
        var tap = Require(again, "F");
        var swipe = Require(again, "W");
        Assert.Equal("Round", again.ProfileName);
        Assert.Equal(10, tap.TargetX);
        Assert.Equal(4, swipe.EndY);
    }

    private static KeyMapping Require(KeymapProfile profile, string key)
    {
        Assert.True(profile.TryGet(key, out var mapping));
        return mapping ?? throw new InvalidOperationException(key);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NexusVM.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("NexusVM.sln was not found above the test output.");
    }
}
