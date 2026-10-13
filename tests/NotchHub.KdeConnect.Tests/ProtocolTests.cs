using System.Text.Json.Nodes;
using NotchHub.KdeConnect.Protocol;

namespace NotchHub.KdeConnect.Tests;

public class ProtocolTests
{
    [Fact]
    public void Packet_round_trips_with_payload_info()
    {
        var p = new NetworkPacket(NetworkPacket.Notification, new JsonObject { ["id"] = "k1", ["title"] = "Zażółć \"gęślą\"\njaźń" })
        {
            PayloadSize = 1234,
            PayloadPort = 1739,
        };
        var line = p.Serialize();
        Assert.EndsWith("\n", line);
        Assert.Single(line.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var q = NetworkPacket.TryParse(line.TrimEnd('\n'))!;
        Assert.Equal(p.Id, q.Id);
        Assert.Equal(NetworkPacket.Notification, q.Type);
        Assert.Equal("Zażółć \"gęślą\"\njaźń", q.GetString("title"));
        Assert.Equal(1234, q.PayloadSize);
        Assert.Equal(1739, q.PayloadPort);
        Assert.True(q.HasPayload);
    }

    [Fact]
    public void Lenient_accessors_accept_strings_for_numbers()
    {
        var q = NetworkPacket.TryParse("""{"id":"1700000000000","type":"kdeconnect.notification","body":{"time":"1700000000123","silent":"true","isClearable":true,"n":5}}""")!;
        Assert.Equal(1700000000000, q.Id);
        Assert.Equal(1700000000123, q.GetLong("time"));
        Assert.True(q.GetBool("silent"));
        Assert.True(q.GetBool("isClearable"));
        Assert.Equal("5", q.GetString("n"));
        Assert.Null(NetworkPacket.TryParse("not json"));
        Assert.Null(NetworkPacket.TryParse("""{"body":{}}"""));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("_ab12_cd34_ef56_0123456789abcdef_ab", true)]
    [InlineData("0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456", false)]
    [InlineData("0123456789abcdef0123456789abcde!", false)]
    [InlineData(null, false)]
    public void Device_id_validation(string? id, bool valid) => Assert.Equal(valid, DeviceIdentity.IsValidDeviceId(id));

    [Fact]
    public void Name_filtering_and_identity_validation()
    {
        Assert.Equal("Pixel 9 Pro", DeviceIdentity.FilterName("Pixel 9 Pro!"));
        Assert.Equal("ab", DeviceIdentity.FilterName("a\"'b,;:.!?()[]<>"));
        Assert.Equal(32, DeviceIdentity.FilterName(new string('x', 50)).Length);

        var good = DeviceIdentity.Build("0123456789abcdef0123456789abcdef", "Desk", tcpPort: 1716);
        Assert.True(DeviceIdentity.IsValidIdentity(NetworkPacket.TryParse(good.Serialize())));
        Assert.Equal("desktop", good.GetString("deviceType"));
        Assert.Equal(8, good.GetInt("protocolVersion"));
        Assert.Contains(NetworkPacket.Notification, good.GetStringList("incomingCapabilities"));
        Assert.Contains(NetworkPacket.FindMyPhone, good.GetStringList("outgoingCapabilities"));

        Assert.False(DeviceIdentity.IsValidIdentity(DeviceIdentity.Build("0123456789abcdef0123456789abcdef", "...")));
        Assert.False(DeviceIdentity.IsValidIdentity(DeviceIdentity.Build("short", "Desk")));
        Assert.True(DeviceIdentity.IsValidDiscovery(DeviceIdentity.Build("0123456789abcdef0123456789abcdef", "")));
    }

    [Fact]
    public void Certificate_and_verification_key()
    {
        using var a = CertificateHelper.Create("0123456789abcdef0123456789abcdef");
        using var b = CertificateHelper.Create("fedcba9876543210fedcba9876543210");
        Assert.True(a.HasPrivateKey);
        Assert.Equal("0123456789abcdef0123456789abcdef", CertificateHelper.CommonName(a));
        Assert.Contains("O=KDE", a.Subject);
        Assert.Contains("OU=Kde connect", a.Subject);
        var k1 = CertificateHelper.VerificationKey(a, b, 1700000000);
        var k2 = CertificateHelper.VerificationKey(b, a, 1700000000);
        Assert.Equal(k1, k2);
        Assert.Matches("^[0-9A-F]{8}$", k1);
        Assert.NotEqual(k1, CertificateHelper.VerificationKey(a, b, 1700000001));
    }
}
