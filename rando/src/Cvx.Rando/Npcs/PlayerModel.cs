using System.Buffers.Binary;
using Cvx.Formats;
using Cvx.Formats.Models;

namespace Cvx.Rando.Npcs;

/// <summary>
/// The players' gameplay models: SYSTEM.AFS player files as <c>bhReadPlayerData</c> (dread.c)
/// reads them: <c>[u32 size][model page][u32 size][motions][u32 size][a third block]</c>, then
/// for each model of the page that has a texture list <c>[u32 size][texture entry]</c> (the top
/// bit of the size: the entry starts at the next 32 bytes), then bytes the loader does not read.
/// The page: a SKIN block and the body (22 objects, <see cref="CharacterRig.Player"/>), two basic
/// models, a 22-object model with no polygons, and for some costumes more pieces (Claire's
/// ponytail...), each textured one with its own copy of the textures.
/// </summary>
public static class PlayerModel
{
    public const string Archive = "SYSTEM.AFS";

    /// <summary>The player files of a character: Claire (10, and 14 for her other costume), Chris (11).</summary>
    public static IReadOnlyList<int> EntriesOf(string player) => player == "Chris" ? [11] : [10, 14];

    /// <summary>
    /// The texture global indexes of the converted bodies: past the NPCs' (<see cref="CharacterConverter.FirstGlobalIndex"/>
    /// and 16 per NPC or player in the rooms), one range per file.
    /// </summary>
    public static uint GlobalIndexOf(int entry) => CharacterConverter.FirstGlobalIndex + 0x200 + (uint)entry * 16;

    /// <summary>
    /// The models after the body that hold its blinking textures: the player blinks by giving
    /// the body model 1's and model 2's texture lists (player.c, <c>PlyEyeTab</c>: <c>txp[1]</c>,
    /// <c>txp[2]</c>), the same textures with the eyes half and fully closed. Their texture
    /// entries are the 2nd and 3rd.
    /// </summary>
    public const int BlinkModels = 2;

    /// <summary>
    /// Claire's ponytail: model 4, the 4-object chain (with its own SKIN block) the game hangs
    /// on her head as a linked object with its own motion (player.c, <c>lkmtab</c>, <c>mdlver</c>
    /// 4; objitm.c, <c>bhCalcHair</c>). Chris's files have no model 4.
    /// </summary>
    public const int PonytailModel = 4;

    /// <summary>
    /// The file with its body made from <paramref name="pld"/> (its SKIN block dropped: the converted
    /// body's parts ride their objects) and the body's textures replaced; everything else as it was.
    /// </summary>
    public static byte[] Replace(byte[] file, Pld pld, uint globalIndex)
    {
        var layout = Layout.Read(file);
        var bodyBlock = layout.Page.Blocks.FindIndex(b => b.Model != null);
        if (bodyBlock < 0 || layout.Page.Blocks[bodyBlock].Model!.Objects.Count != CharacterRig.Player.Objects)
            throw new InvalidDataException("The player file's first model is not a 22-object body.");
        var body = layout.Page.Blocks[bodyBlock].Model!;
        var bodyTextures = layout.Textures[0];
        var template = TextureEntry.Read(bodyTextures.Data).Pictures.First(p => p.Width == 256 && p.Height == 256 && p.ImageType == 5);
        var converted = CharacterConverter.Convert(pld, body, template, globalIndex, CharacterRig.Player);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // the page: the body's SKIN block left out, the body converted, the ponytail hidden, the
        // rest as they were
        using (var page = new MemoryStream())
        using (var pw = new BinaryWriter(page))
        {
            var model = -1;
            foreach (var (block, i) in layout.Page.Blocks.Select((b, i) => (b, i)))
            {
                if (block.Kind == ModelPageBlockKind.Model)
                    model++;
                if (i == bodyBlock - 1 && block.Kind == ModelPageBlockKind.Skin)
                    continue;
                if (block.Kind == ModelPageBlockKind.Empty)
                {
                    pw.Write(0);
                    continue;
                }
                byte[] data;
                if (i == bodyBlock)
                {
                    data = converted.Page.Blocks.Single().Model!.ToBytes((int)page.Position + 4);
                }
                else if (block.Kind == ModelPageBlockKind.Model && model == PonytailModel && block.Model is { } hair)
                {
                    foreach (var o in hair.Objects)
                        o.EvalFlags |= NjObject.EvalHide;
                    data = hair.ToBytes((int)page.Position + 4);
                }
                else
                {
                    data = block.Data;
                }
                pw.Write(data.Length);
                pw.Write(data);
            }
            pw.Write(-1);
            pw.Write(layout.Page.Tail);
            pw.Flush();
            bw.Write((int)page.Length);
            bw.Write(page.ToArray());
        }

        // the motions and the third block as they were
        bw.Write(file, layout.MotionsAt, layout.TexturesAt - layout.MotionsAt);

        // the textures: the body's converted, and the blinks' (the same pictures: a blink then
        // changes nothing), each entry where the loader looks for it
        var textures = converted.Textures.ToBytes();
        foreach (var (texture, i) in layout.Textures.Select((t, i) => (t, i)))
        {
            var data = i <= BlinkModels ? textures : texture.Data;
            bw.Write(data.Length | (texture.Aligned ? unchecked((int)0x80000000) : 0));
            if (texture.Aligned)
            {
                while (ms.Position % 32 != 0)
                    bw.Write((byte)0);
            }
            bw.Write(data);
        }
        bw.Write(file, layout.TailAt, file.Length - layout.TailAt);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>Where the parts of a player file are.</summary>
    public sealed record Layout(ModelPage Page, int MotionsAt, int TexturesAt, IReadOnlyList<(byte[] Data, bool Aligned)> Textures, int TailAt)
    {
        public static Layout Read(byte[] file)
        {
            int U(int at) => BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at));
            var pageSize = U(0);
            var page = ModelPage.Read(file.AsSpan(4, pageSize));
            var motionsAt = 4 + pageSize;
            var p = motionsAt;
            p += 4 + U(p);      // motions
            p += 4 + U(p);      // the third block
            var texturesAt = p;
            // one entry per model with a texture list, in order (the basic models' lists are not
            // read here, so the entries are taken while they look like ones: a size, then TIM2 records);
            // the body's comes first
            var textures = new List<(byte[], bool)>();
            while (p + 4 <= file.Length)
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(p));
                var aligned = (size & 0x80000000) != 0;
                var at = aligned ? (p + 4 + 31) & ~31 : p + 4;
                size &= 0x7FFFFFFF;
                if (size < 32 || at + size > file.Length || BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at)) != TextureRecord.Tim2Code)
                    break;
                textures.Add((file.AsSpan(at, (int)size).ToArray(), aligned));
                p = at + (int)size;
            }
            if (textures.Count == 0)
                throw new InvalidDataException("The player file has no textures for its body.");
            return new Layout(page, motionsAt, texturesAt, textures, p);
        }
    }
}
