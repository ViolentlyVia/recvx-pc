using Cvx.Formats;
using Cvx.Formats.Rdx;
using Cvx.Rando;
using Cvx.Rando.Data;
using Cvx.Rando.Items;
using Cvx.Rando.Npcs;

namespace Cvx.Tests;

public class NpcTests
{
    private static readonly RandoData Data = RandoData.Load();

    [Fact]
    public void TheCastLeavesClaireAndChrisAlone()
    {
        // their cutscene models (91, 92) always stay: the player looks like themselves
        var types = Data.Npcs.Characters.SelectMany(c => c.Types).ToList();
        Assert.DoesNotContain(91, types);
        Assert.DoesNotContain(92, types);
        Assert.Equal(types.Count, types.Distinct().Count());
        var voices = Data.Npcs.Characters.Select(c => c.Voice).ToHashSet();
        Assert.All(Data.Npcs.Voices.Values.SelectMany(d => d.Values), v => Assert.Contains(v, voices));
    }

    [GameDataFact]
    public void EveryoneTakesAnotherLookAndVoice()
    {
        using var rooms = RoomArchive.Open(TestData.Open());
        var looksOf = Data.Npcs.Characters.ToDictionary(c => c.Name);
        foreach (var seed in new[] { 1, 2 })
        {
            var patcher = new ItemPatcher(Data, rooms);
            var result = new NpcRandomizer(Data, rooms, Rng.For(seed, "npcs")).Apply(patcher);
            Assert.All(result.Looks, x => Assert.NotEqual(x.Key, x.Value));
            Assert.Equal(result.Looks.Count, result.Looks.Values.Distinct().Count());
            Assert.Empty(result.KeptRooms);

            // each swapped line now has a line of the character whose look the speaker took
            var speaker = Data.Npcs.Voices.SelectMany(d => d.Value.Select(v => ((int.Parse(d.Key), v.Key), v.Value))).ToDictionary(x => x.Item1, x => x.Value);
            foreach (var v in result.Voices)
            {
                var who = Data.Npcs.Characters.Single(c => c.Voice == v.Speaker).Name;
                Assert.Equal(looksOf[result.Looks[who]].Voice, speaker[(v.FromDisc, v.FromEntry)]);
            }

            // and the rooms' entries have the new look's model
            var changed = patcher.ChangedRooms;
            foreach (var id in result.ChangedRooms.Take(8))
            {
                var after = RdxEnemyLayout.Read(changed[id]);
                Assert.True(after.IsConsistent, $"{id}");
                foreach (var e in after.Enemies)
                {
                    var who = Data.Npcs.Characters.FirstOrDefault(c => c.Types.Contains(e.Type));
                    if (who == null)
                        continue;
                    var look = looksOf[result.Looks[who.Name]].Look.Split(':');
                    var donor = RdxEnemyLayout.Read(rooms.ReadRoom(RoomId.ParseName(look[0]))).Enemies[int.Parse(look[1])];
                    Assert.True(e.Model.Span.SequenceEqual(donor.Model.Span), $"{id} enemy {e.Index}");
                }
            }
        }
    }
}
