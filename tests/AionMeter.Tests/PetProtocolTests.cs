using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.Protocol;

namespace AionMeter.Tests;

public class PetProtocolTests
{
    private static List<GameEvent> Parse(ushort opcode, string hex)
    {
        var events = new List<GameEvent>();
        var parser = new PacketParser(GameData.Empty, events.Add) { TimeMs = 1_000 };
        parser.Handle(opcode, Wire.FromHex(hex.Replace(" ", "")));
        return events;
    }

    // Layout of the login snapshot seen on EU 2026-10-12: active 1046; owned 1018 (level 2) and 1054 (level 3);
    // souls 1018 = 11, 1010 = 1 (not owned yet); then the genus insight blocks, which the parser skips.
    private const string Collection =
        "16040000" +
        "02" + "fa030000 fa030000 02000000" + "1e040000 1e040000 03000000" +
        "02" + "fa030000 0b000000" + "f2030000 01000000" +
        "0601c8000000027b020000";

    [Fact]
    public void Collection_snapshot_gives_levels_and_souls()
    {
        var e = Assert.IsType<PetCollectionEvent>(Assert.Single(Parse(Opcodes.PetCollection, Collection)));
        Assert.Equal(1046, e.ActivePetId);
        Assert.Equal(new Dictionary<int, int> { [1018] = 2, [1054] = 3 }, e.Levels);
        Assert.Equal(new Dictionary<int, int> { [1018] = 11, [1010] = 1 }, e.Souls);
    }

    [Fact]
    public void Collection_with_a_broken_entry_is_dropped()
    {
        // Second pet id of the owned pair disagrees with the first.
        Assert.Empty(Parse(Opcodes.PetCollection, "16040000 01 fa030000 fb030000 02000000 00"));
    }

    [Fact]
    public void Soul_drop_gives_pet_and_amount()
    {
        var e = Assert.IsType<PetSoulGainedEvent>(Assert.Single(Parse(Opcodes.PetSoul, "01 fa030000 01000000")));
        Assert.Equal(1018, e.PetId);
        Assert.Equal(1, e.Souls);
    }

    [Theory]
    [InlineData("01 fa030000 0100")]                 // short
    [InlineData("01 05000000 01000000")]             // not a pet id
    [InlineData("00")]                               // empty
    public void Soul_drop_rejects_odd_layouts(string hex) => Assert.Empty(Parse(Opcodes.PetSoul, hex));
}
