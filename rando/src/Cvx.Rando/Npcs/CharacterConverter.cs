using System.Numerics;
using Cvx.Formats.Models;

namespace Cvx.Rando.Npcs;

/// <summary>A converted character: a model page for an NPC's enemy entry and its texture entry.</summary>
public sealed record ConvertedCharacter(ModelPage Page, TextureEntry Textures);

/// <summary>
/// Turns an RE2 player model (BioRand's characters) into a model page for Code: Veronica's
/// human NPCs. The NPCs share one skeleton of 27 objects (see PLAN.md, "The NPC skeleton"),
/// and their motions come from outside the room, so the new page keeps the target's objects
/// (tree, bind positions, eval flags) and puts each rigid RE2 part on the object it rides on.
/// No SKIN block (each part moves with its object, <c>EasyMultiDrawTreeCnk</c>) and no MASK
/// block (the face code does nothing without one, <c>bhInitMask</c>).
/// </summary>
/// <remarks>
/// Placement: RE2 is Y-down, facing +X, right side -Z, arms hanging down; CV is Y-up, facing
/// -Z, right side +X, arms out (T-pose). A part's vertices are turned into CV's axes, scaled
/// by the ratio of the two skeletons' leg lengths, put at the CV object that matches the RE2
/// joint, and limbs are turned so each bone points the way CV's does.
/// </remarks>
public static class CharacterConverter
{
    /// <summary>
    /// RE2 part -> the CV object it rides on, the CV object at its joint, and for limbs the RE2
    /// part and CV object its bone points to (null: keep the part's orientation; the hands use
    /// their forearm's).
    /// </summary>
    private static readonly (int Part, int Object, int Joint, (int Part, int Object)? Bone)[] Map =
    [
        (0, 3, 2, null),        // torso: rides the chest, joint at the waist
        (1, 1, 1, null),        // pelvis
        (2, 19, 19, (3, 20)),   // right thigh, shin, foot
        (3, 20, 20, (4, 21)),
        (4, 21, 21, null),
        (5, 23, 23, (6, 24)),   // left thigh, shin, foot
        (6, 24, 24, (7, 25)),
        (7, 25, 25, null),
        (8, 5, 4, null),        // head: rides the head, joint at the neck
        (9, 12, 12, (10, 13)),  // right upper arm, forearm, hand
        (10, 13, 13, (11, 14)),
        (11, 14, 14, null),
        (12, 16, 16, (13, 17)), // left upper arm, forearm, hand
        (13, 17, 17, (14, 18)),
        (14, 18, 18, null),
    ];

    private static readonly Dictionary<int, int> HandOf = new() { [11] = 10, [14] = 13 };

    /// <summary>The objects of the human NPC skeleton (types 91-98).</summary>
    public const int SkeletonObjects = 27;

    /// <summary>
    /// The texture global indexes converted characters use: the game's own are below 0x80000,
    /// and textures with the same index are shared by the game's texture cache.
    /// </summary>
    public const uint FirstGlobalIndex = 0x3A00000;

    /// <param name="skeleton">The target NPC's model (its objects are kept).</param>
    /// <param name="template">A 256x256 8-bit texture of the game (its TIM2 head is reused).</param>
    /// <param name="globalIndex">The global index of the first texture; the others follow.</param>
    public static ConvertedCharacter Convert(Pld pld, NjModel skeleton, Tim2Picture template, uint globalIndex = FirstGlobalIndex)
    {
        if (skeleton.Objects.Count != SkeletonObjects)
            throw new ArgumentException($"The target has {skeleton.Objects.Count} objects, not the human NPC skeleton's {SkeletonObjects}.", nameof(skeleton));
        if (pld.Armature.Count < 15 || pld.Mesh.Parts.Count < 15)
            throw new InvalidDataException($"The PLD has {pld.Armature.Count} joints and {pld.Mesh.Parts.Count} parts, not RE2's 15.");

        var bind = skeleton.BindPositions();
        var rest = pld.Armature.RestPositions();
        var scale = (Leg(bind, 19, 21) + Leg(bind, 23, 25)) / (Leg(rest, 2, 4) + Leg(rest, 5, 7));

        var rotations = new Dictionary<int, Quaternion>();
        foreach (var (part, obj, _, bone) in Map)
        {
            rotations[part] = bone is var (childPart, childObj)
                ? Turn(Vector3.Normalize(ToCv(rest[childPart] - rest[part])), Vector3.Normalize(bind[childObj] - bind[obj]))
                : Quaternion.Identity;
        }
        foreach (var (hand, forearm) in HandOf)
            rotations[hand] = rotations[forearm];

        // textures: one per (page, CLUT) the parts use
        var tim = pld.Texture;
        var keys = new List<(int Page, int Clut)>();
        int TextureOf(Md1Polygon p)
        {
            var key = (p.Page, Math.Clamp((p.Clut >> 6) - tim.ClutY, 0, tim.Cluts.Length - 1));
            var i = keys.IndexOf(key);
            if (i < 0)
            {
                keys.Add(key);
                i = keys.Count - 1;
            }
            return i;
        }

        var templateModel = skeleton.Objects.Select(o => o.Model).OfType<NjChunkModel>().FirstOrDefault();
        var material = templateModel?.Polygons?.FirstOrDefault(c => c.Kind == NjPolyKind.Material) ?? NjPolyChunk.Material(0xFF7F7F7F, 0xFF7F7F7F);
        var stripBits = templateModel?.Polygons?.FirstOrDefault(c => c.Kind == NjPolyKind.Strip)?.HeadBits ?? 0x02;

        var model = new NjModel { Status = skeleton.Status, Textures = [] };
        foreach (var o in skeleton.Objects)
        {
            model.Objects.Add(new NjObject
            {
                EvalFlags = o.EvalFlags,
                Position = o.Position,
                Angle = o.Angle,
                Scale = o.Scale,
                Child = o.Child,
                Sibling = o.Sibling,
            });
        }

        foreach (var (part, obj, joint, _) in Map)
        {
            var source = pld.Mesh.Parts[part];
            if (source.Polygons.Count == 0)
                continue;
            var turn = rotations[part];
            var offset = bind[joint] - bind[obj];
            var vertices = new List<(Vector3 Position, Vector3 Normal)>();
            var index = new Dictionary<(int, int), int>();
            int VertexOf(int v, int n)
            {
                if (!index.TryGetValue((v, n), out var i))
                {
                    var normal = n < source.Normals.Length ? source.Normals[n] : Vector3.UnitY;
                    var nrm = Vector3.Transform(ToCv(normal), turn);
                    vertices.Add((offset + Vector3.Transform(ToCv(source.Positions[v]) * scale, turn),
                                  nrm == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(nrm)));
                    index[(v, n)] = i = vertices.Count - 1;
                }
                return i;
            }

            var strips = new SortedDictionary<int, List<NjStrip>>();
            foreach (var polygon in source.Polygons)
            {
                var texture = TextureOf(polygon);
                var corners = polygon.Vertices.Select((v, k) => VertexOf(v, polygon.Normals[k])).ToArray();
                var strip = corners.Select((c, k) => new NjStripVertex((ushort)c, (short)(polygon.Uvs[k].U * 4), (short)(polygon.Uvs[k].V * 4))).ToList();
                // CV's winding: a strip's even triangles (a, b, c) face along cross(b - a, c - a)
                var (a, b, c) = (vertices[corners[0]], vertices[corners[1]], vertices[corners[2]]);
                var face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
                var flipped = Vector3.Dot(face, a.Normal + b.Normal + c.Normal) < 0;
                if (!strips.TryGetValue(texture, out var list))
                    strips[texture] = list = [];
                list.Add(new NjStrip(flipped, strip));
            }

            var polygons = new List<NjPolyChunk> { material };
            foreach (var (texture, list) in strips)
            {
                polygons.Add(NjPolyChunk.Texture(texture));
                polygons.Add(NjStrip.Write(NjPolyChunk.StripUvhType, stripBits, list));
            }
            var min = vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var max = vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            var center = (min + max) / 2;
            var target = model.Objects[obj];
            target.Model = new NjChunkModel
            {
                Vertices = [NjVertexChunk.Ps2(vertices)],
                Polygons = polygons,
                Center = center,
                Radius = Math.Max(0.01f, vertices.Max(v => Vector3.Distance(v.Position, center))),
            };
            target.EvalFlags &= ~NjObject.EvalHide;
        }

        var pictures = new List<Tim2Picture>();
        for (var t = 0; t < keys.Count; t++)
        {
            model.Textures!.Add($"bio{t:00}");
            pictures.Add(PageTexture(tim, keys[t].Page, keys[t].Clut, template, globalIndex + (uint)t));
        }

        var page = new ModelPage();
        page.Blocks.Add(ModelPageBlock.FromModel(model));
        return new ConvertedCharacter(page, TextureEntry.FromPictures(pictures));
    }

    /// <summary>
    /// A texture page as a 256x256 8-bit picture: the page's pixels on the left (a page is 128
    /// pixels wide at 8 bits, 256 at 4), its last column repeated to the right edge.
    /// </summary>
    private static Tim2Picture PageTexture(PsxTim tim, int page, int clut, Tim2Picture template, uint globalIndex)
    {
        const int Size = 256;
        var width = tim.PageWidth;
        var pixels = new byte[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var sx = page * width + Math.Min(x, width - 1);
                var sy = Math.Min(y, tim.Height - 1);
                pixels[y * Size + x] = sx < tim.Width ? tim.Pixels[sy * tim.Width + sx] : (byte)0;
            }
        }
        var palette = new uint[256];
        var colors = tim.Cluts[clut];
        for (var i = 0; i < colors.Length && i < 256; i++)
        {
            var c = colors[i];
            uint r = (uint)(c & 0x1F) * 255 / 31, g = (uint)((c >> 5) & 0x1F) * 255 / 31, b = (uint)((c >> 10) & 0x1F) * 255 / 31;
            // PS1 colour 0 is see-through; the PS2's alpha is 0-0x80
            palette[i] = c == 0 ? 0 : r | g << 8 | b << 16 | 0x80u << 24;
        }
        return Tim2Picture.Create(template, globalIndex, Size, Size, pixels, palette);
    }

    /// <summary>RE2's axes to CV's: (x, y, z) -> (-z, -y, -x), a rotation (no mirroring).</summary>
    private static Vector3 ToCv(Vector3 v) => new(-v.Z, -v.Y, -v.X);

    private static float Leg(Vector3[] p, int hip, int ankle) => Vector3.Distance(p[hip], p[ankle]);

    /// <summary>The shortest turn taking direction <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Quaternion Turn(Vector3 from, Vector3 to)
    {
        var dot = Vector3.Dot(from, to);
        if (dot > 0.99999f)
            return Quaternion.Identity;
        if (dot < -0.99999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(from, to));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }
}
