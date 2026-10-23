using System.Numerics;
using Cvx.Formats.Models;

namespace Cvx.Rando.Npcs;

/// <summary>A converted character: a model page for an NPC's enemy entry and its texture entry.</summary>
public sealed record ConvertedCharacter(ModelPage Page, TextureEntry Textures);

/// <summary>
/// A CV skeleton the converter can dress: how many objects it has, which object each RE2 part
/// rides on (the object at its joint, and for limbs the part and object its bone points to),
/// and the objects at the joints <c>Fit</c> moves to the character's proportions.
/// </summary>
public sealed record CharacterRig(
    int Objects,
    (int Part, int Object, int Joint, (int Part, int Object)? Bone)[] Map,
    int Pelvis, int Neck,
    int RightShoulder, int RightElbow, int RightWrist, int LeftShoulder, int LeftElbow, int LeftWrist,
    int RightHip, int RightKnee, int RightAnkle, int LeftHip, int LeftKnee, int LeftAnkle)
{
    /// <summary>
    /// The human NPCs' skeleton (types 91-98, 27 objects, see PLAN.md "The NPC skeleton"). The
    /// hands ride the forearms, placed at the wrists: the game's NPCs move their hands with the
    /// forearm (their wrist objects, 14 and 18, have no SKIN entry and no vertices), so the
    /// motions do not pose the wrists, and a hand on one turns at random.
    /// </summary>
    public static readonly CharacterRig Npc = new(27,
    [
        (0, 3, 1, null),        // torso: rides the chest, joint placed from the pelvis (see Convert)
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
        (11, 13, 14, null),
        (12, 16, 16, (13, 17)), // left upper arm, forearm, hand
        (13, 17, 17, (14, 18)),
        (14, 17, 18, null),
    ], Pelvis: 1, Neck: 4, RightShoulder: 12, RightElbow: 13, RightWrist: 14, LeftShoulder: 16, LeftElbow: 17, LeftWrist: 18,
       RightHip: 19, RightKnee: 20, RightAnkle: 21, LeftHip: 23, LeftKnee: 24, LeftAnkle: 25);

    /// <summary>
    /// The players' skeleton (SYSTEM.AFS player files, 22 objects): 1 pelvis, 2 waist, 3 chest,
    /// 4 neck, 5 head; 6-9 right collar, upper arm, forearm, wrist; 10-13 the left arm; 14-17
    /// right hip, knee, ankle, toes; 18-21 the left leg. The wrists (9, 13) and toes have no
    /// vertices in the game's skin, so the hands ride the forearms as on the NPCs.
    /// </summary>
    public static readonly CharacterRig Player = new(22,
    [
        (0, 3, 1, null),        // torso: rides the chest, joint placed from the pelvis
        (1, 1, 1, null),        // pelvis
        (2, 14, 14, (3, 15)),   // right thigh, shin, foot
        (3, 15, 15, (4, 16)),
        (4, 16, 16, null),
        (5, 18, 18, (6, 19)),   // left thigh, shin, foot
        (6, 19, 19, (7, 20)),
        (7, 20, 20, null),
        (8, 5, 4, null),        // head: rides the head, joint at the neck
        (9, 7, 7, (10, 8)),     // right upper arm, forearm, hand
        (10, 8, 8, (11, 9)),
        (11, 8, 9, null),
        (12, 11, 11, (13, 12)), // left upper arm, forearm, hand
        (13, 12, 12, (14, 13)),
        (14, 12, 13, null),
    ], Pelvis: 1, Neck: 4, RightShoulder: 7, RightElbow: 8, RightWrist: 9, LeftShoulder: 11, LeftElbow: 12, LeftWrist: 13,
       RightHip: 14, RightKnee: 15, RightAnkle: 16, LeftHip: 18, LeftKnee: 19, LeftAnkle: 20);
}

/// <summary>
/// Turns an RE2 player model (BioRand's characters) into a model page for Code: Veronica's
/// human NPCs. The NPCs share one skeleton of 27 objects (see PLAN.md, "The NPC skeleton"),
/// and their motions come from outside the room, so the new page keeps the target's object
/// tree (and eval flags) and puts each rigid RE2 part on the object it rides on. No SKIN block
/// (each part moves with its object, <c>EasyMultiDrawTreeCnk</c>) and no MASK block (the face
/// code does nothing without one, <c>bhInitMask</c>).
/// </summary>
/// <remarks>
/// Placement: RE2 is Y-down, facing +X, right side -Z, arms hanging down; CV is Y-up, facing
/// -Z, right side +X, arms out (T-pose). A part's vertices are turned into CV's axes, scaled
/// so the neck stands as high above the ankles as the target's (<see cref="Stature"/>: every
/// character as tall as the NPC, a child too, with their own proportions), put at the CV object that matches the RE2
/// joint, and limbs are turned so each bone points the way CV's does. The joints themselves
/// move to the character's own proportions (<see cref="Fit"/>): the game's motions only turn
/// the objects (only the root has position keys), so the bone lengths are the model's, and a
/// wide character's arms hang from their own shoulders instead of the target's.
/// </remarks>
public static class CharacterConverter
{
    // RE2 part -> the RE2 forearm whose turn the hand takes
    private static readonly Dictionary<int, int> HandOf = new() { [11] = 10, [14] = 13 };

    /// <summary>The objects of the human NPC skeleton (types 91-98).</summary>
    public const int SkeletonObjects = 27;

    /// <summary>
    /// The texture global indexes converted characters use: the game's own are below 0x80000,
    /// and textures with the same index are shared by the game's texture cache.
    /// </summary>
    public const uint FirstGlobalIndex = 0x3A00000;

    /// <param name="skeleton">The target's model (its objects are kept).</param>
    /// <param name="template">A 256x256 8-bit texture of the game (its TIM2 head is reused).</param>
    /// <param name="globalIndex">The global index of the first texture; the others follow.</param>
    /// <param name="rig">The target's skeleton: <see cref="CharacterRig.Npc"/> (the default) or <see cref="CharacterRig.Player"/>.</param>
    public static ConvertedCharacter Convert(Pld pld, NjModel skeleton, Tim2Picture template, uint globalIndex = FirstGlobalIndex, CharacterRig? rig = null)
    {
        rig ??= CharacterRig.Npc;
        if (skeleton.Objects.Count != rig.Objects)
            throw new ArgumentException($"The target has {skeleton.Objects.Count} objects, not the skeleton's {rig.Objects}.", nameof(skeleton));
        if (pld.Armature.Count < 15 || pld.Mesh.Parts.Count < 15)
            throw new InvalidDataException($"The PLD has {pld.Armature.Count} joints and {pld.Mesh.Parts.Count} parts, not RE2's 15.");

        var rest = pld.Armature.RestPositions();
        var target = skeleton.BindPositions();
        var scale = Stature(target, rig.Neck, rig.RightAnkle, rig.LeftAnkle) / Stature(rest.Select(ToCv).ToArray(), 8, 4, 7);
        var bind = Fit(skeleton, target, rest, scale, rig);
        var parents = skeleton.Parents();
        var map = rig.Map;

        var rotations = new Dictionary<int, Quaternion>();
        foreach (var (part, obj, _, bone) in map)
        {
            rotations[part] = bone is var (childPart, childObj)
                ? Turn(Vector3.Normalize(ToCv(rest[childPart] - rest[part])), Vector3.Normalize(bind[childObj] - bind[obj]))
                : Quaternion.Identity;
        }
        foreach (var (hand, forearm) in HandOf)
            rotations[hand] = rotations[forearm];

        // textures: one per page the parts use, each with its own CLUT (see Md1Polygon)
        var tim = pld.Texture;
        var keys = new List<(int Page, int Clut)>();
        int TextureOf(Md1Polygon p)
        {
            var key = (p.Page, Math.Min(p.Page, tim.Cluts.Length - 1));
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
        foreach (var (o, i) in skeleton.Objects.Select((o, i) => (o, i)))
        {
            // the fitted joints, unscaled (the parts are placed for scale 1)
            model.Objects.Add(new NjObject
            {
                EvalFlags = o.EvalFlags,
                Position = parents[i] == -1 ? bind[i] : bind[i] - bind[parents[i]],
                Angle = o.Angle,
                Child = o.Child,
                Sibling = o.Sibling,
            });
        }

        // one model per object: a forearm's holds the forearm and the hand
        foreach (var group in map.GroupBy(m => m.Object))
        {
            var obj = group.Key;
            var vertices = new List<(Vector3 Position, Vector3 Normal)>();
            var strips = new SortedDictionary<int, List<NjStrip>>();
            foreach (var (part, _, joint, _) in group)
            {
                var source = pld.Mesh.Parts[part];
                var turn = rotations[part];
                // the torso's joint is RE2's root, placed where it is on the pelvis (most characters
                // have the two together; some, like RE3's Nemesis, have the root well below)
                var at = part == 0 ? bind[rig.Pelvis] + ToCv(rest[0] - rest[1]) * scale : bind[joint];
                var offset = at - bind[obj];
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
            }
            if (strips.Count == 0)
                continue;

            var polygons = new List<NjPolyChunk> { material };
            foreach (var (texture, list) in strips)
            {
                polygons.Add(NjPolyChunk.Texture(texture));
                polygons.Add(NjStrip.Write(NjPolyChunk.StripUvhType, stripBits, list));
            }
            var min = vertices.Select(v => v.Position).Aggregate(Vector3.Min);
            var max = vertices.Select(v => v.Position).Aggregate(Vector3.Max);
            var center = (min + max) / 2;
            var holder = model.Objects[obj];
            holder.Model = new NjChunkModel
            {
                Vertices = [NjVertexChunk.Ps2(vertices)],
                Polygons = polygons,
                Center = center,
                Radius = Math.Max(0.01f, vertices.Max(v => Vector3.Distance(v.Position, center))),
            };
            holder.EvalFlags &= ~NjObject.EvalHide;
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
    /// The target's joints moved to the character's proportions: the neck, shoulders and hips
    /// where the character's are from their pelvis (the target's hips object), and each
    /// arm and leg bone the character's length along the target's direction. Then the whole
    /// body is moved up or down so the ankles stay at the target's height (the feet on the
    /// ground). Collarbones, toes and the face's objects keep their places relative to their
    /// parents.
    /// </summary>
    private static Vector3[] Fit(NjModel skeleton, Vector3[] target, Vector3[] rest, float scale, CharacterRig r)
    {
        var p = (Vector3[])target.Clone();
        var parents = skeleton.Parents();
        var under = Enumerable.Range(0, p.Length).Select(i =>
            Enumerable.Range(0, p.Length).Where(j => Above(parents, i, j)).ToArray()).ToArray();
        void Move(int obj, Vector3 to)
        {
            var d = to - p[obj];
            foreach (var j in under[obj])
                p[j] += d;
        }
        Vector3 Re2(int part, int from) => ToCv(rest[part] - rest[from]) * scale;
        void Bone(int obj, int parentObj, int part, int parentPart) =>
            Move(obj, p[parentObj] + Vector3.Normalize(target[obj] - target[parentObj]) * Vector3.Distance(rest[part], rest[parentPart]) * scale);

        var pelvis = r.Pelvis;
        Move(r.Neck, p[pelvis] + Re2(8, 1));            // neck: the head's joint (from the pelvis)
        Move(r.RightShoulder, p[pelvis] + Re2(9, 1));   // shoulders
        Move(r.LeftShoulder, p[pelvis] + Re2(12, 1));
        Bone(r.RightElbow, r.RightShoulder, 10, 9);     // elbows, wrists
        Bone(r.RightWrist, r.RightElbow, 11, 10);
        Bone(r.LeftElbow, r.LeftShoulder, 13, 12);
        Bone(r.LeftWrist, r.LeftElbow, 14, 13);
        Move(r.RightHip, p[pelvis] + Re2(2, 1));        // hips (from the pelvis)
        Move(r.LeftHip, p[pelvis] + Re2(5, 1));
        Bone(r.RightKnee, r.RightHip, 3, 2);            // knees, ankles
        Bone(r.RightAnkle, r.RightKnee, 4, 3);
        Bone(r.LeftKnee, r.LeftHip, 6, 5);
        Bone(r.LeftAnkle, r.LeftKnee, 7, 6);
        var lift = (target[r.RightAnkle].Y + target[r.LeftAnkle].Y - p[r.RightAnkle].Y - p[r.LeftAnkle].Y) / 2;
        Move(pelvis, p[pelvis] + new Vector3(0, lift, 0));
        return p;
    }

    /// <summary>Whether <paramref name="obj"/> is <paramref name="j"/> or above it in the tree.</summary>
    private static bool Above(int[] parents, int obj, int j)
    {
        for (; j != -1; j = parents[j])
        {
            if (j == obj)
                return true;
        }
        return false;
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

    /// <summary>How high the neck is above the ankles (CV's axes, Y up).</summary>
    public static float Stature(Vector3[] p, int neck, int rightAnkle, int leftAnkle) => p[neck].Y - (p[rightAnkle].Y + p[leftAnkle].Y) / 2;

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
