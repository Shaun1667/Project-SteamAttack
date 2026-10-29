# 근정전 파츠 조립 (Blender 5.2)
# 파츠마다: glb 불러오기 → 하나로 합치기 → 떠 있는 작은 조각 지우기 → (가장 큰 덩어리만 쓰기 옵션)
#           → 긴 쪽을 X로 돌리기 → 지정한 상자(가로 X, 세로 Y, 높이 Z, 미터)에 맞춰 크기 조절 → 아래 파츠 위에 쌓기
# 사용: blender -b --python assemble.py -- <dir> <out.blend> <preview_prefix>
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
D, OUT, PREV = argv[0], argv[1], argv[2]

# name, file, box(x,y,z), place: ("on", 아래파츠) 또는 ("at", (x,y,z)), yaw(도), largest_only, uniform
PARTS = [
    ("하월대",   "lowerbase", (46, 38, 1.6), ("at", (0, 0, 0)), 0, True, False),
    ("상월대",   "upper",     (36, 28, 2.0), ("on", "하월대", (0, 2.0, 0)), 0, False, False),
    ("벽_기둥",  "walls",     (28, 18, 8.0), ("on", "상월대", (0, 3.0, 0)), 0, False, False),
    ("지붕",     "roof",      (36, 25, 11.0), ("on", "벽_기둥", (0, 0, -0.6)), 0, False, False),
    ("어좌",     "throne",    (7, 5, 6.0), ("on", "상월대", (0, 8.5, 0)), 0, False, True),
    ("계단",     "stairs",    (10, 7, 1.6), ("front", "하월대"), 0, False, False),
    ("조정",     "courtyard", (60, 44, 1.0), ("at", (0, -41, -1.0)), 0, True, False),   # 앞마당 (윗면이 땅 높이)
]
YAW = {}   # 미리보기 뒤 방향 고치기: {"계단": 180}
if len(argv) > 3:
    for kv in argv[3:]:
        k, v = kv.split("="); YAW[k] = float(v)

bpy.ops.wm.read_factory_settings(use_empty=True)
placed = {}

def clean_import(path, largest_only):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    ms = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in ms: o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.data.objects):
        if o not in before and o != obj and o.type != "MESH": bpy.data.objects.remove(o, do_unlink=True)
    me = obj.data
    # 덩어리 세기 (합친 사본) → 원본 점 짝짓기
    bmc = bmesh.new(); bmc.from_mesh(me); bmesh.ops.remove_doubles(bmc, verts=bmc.verts, dist=1e-4); bmc.verts.ensure_lookup_table()
    comp = [-1] * len(bmc.verts); sizes = []
    for v in bmc.verts:
        if comp[v.index] >= 0: continue
        cid = len(sizes); st = [v]; comp[v.index] = cid; n = 0
        while st:
            x = st.pop(); n += 1
            for e in x.link_edges:
                y = e.other_vert(x)
                if comp[y.index] < 0: comp[y.index] = cid; st.append(y)
        sizes.append(n)
    kd = KDTree(len(bmc.verts))
    for v in bmc.verts: kd.insert(v.co, v.index)
    kd.balance()
    total = len(bmc.verts); bmc.free()
    big = int(np.argmax(sizes))
    drop = {i for i, n in enumerate(sizes) if (i != big if largest_only else n < total * 0.01)}
    bmo = bmesh.new(); bmo.from_mesh(me)
    rm = [v for v in bmo.verts if comp[kd.find(v.co)[1]] in drop]
    bmesh.ops.delete(bmo, geom=rm, context="VERTS"); bmo.to_mesh(me); bmo.free()
    return obj

def bounds(obj):
    vs = np.array([obj.matrix_world @ v.co for v in obj.data.vertices])
    return vs.min(0), vs.max(0)

for name, f, box, place, yaw, largest, uniform in PARTS:
    path = f"{D}/{f}.glb"
    if not os.path.exists(path): print("SKIP", name, "(파일 없음)"); continue
    obj = clean_import(path, largest)
    obj.name = name
    mn, mx = bounds(obj); size = mx - mn
    # 긴 쪽을 X로
    if size[1] > size[0] * 1.15:
        obj.data.transform(Matrix.Rotation(math.radians(90), 4, "Z")); mn, mx = bounds(obj); size = mx - mn
    y = YAW.get(name, yaw)
    if y: obj.data.transform(Matrix.Rotation(math.radians(y), 4, "Z")); mn, mx = bounds(obj); size = mx - mn
    # 바닥 가운데 기준으로 옮기고 상자에 맞춤
    c = Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2]))
    obj.data.transform(Matrix.Translation(-c))
    if uniform:
        s = min(box[0] / size[0], box[1] / size[1], box[2] / size[2]); sc = (s, s, s)
    else:
        sc = (box[0] / size[0], box[1] / size[1], box[2] / size[2])
    obj.data.transform(Matrix.Diagonal((*sc, 1)))
    # 놓기
    if place[0] == "at":
        obj.location = place[1]
    elif place[0] == "on":
        below = placed.get(place[1]) or placed["하월대"]   # 아직 없는 파츠면 하월대 위에
        bmn, bmx = bounds(below)
        off = place[2]
        obj.location = ((bmn[0] + bmx[0]) / 2 + off[0], (bmn[1] + bmx[1]) / 2 + off[1], bmx[2] + off[2])
    elif place[0] == "front":
        below = placed[place[1]]; bmn, bmx = bounds(below)
        omn, omx = bounds(obj)
        obj.location = ((bmn[0] + bmx[0]) / 2, bmn[1] - (omx[1] - omn[1]) / 2 + 0.3, bmn[2])
    bpy.context.view_layer.update()
    placed[name] = obj
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    mn, mx = bounds(obj)
    print("PLACED", name, "tris", tris, "min", np.round(mn, 1).tolist(), "max", np.round(mx, 1).tolist())

bpy.ops.wm.save_as_mainfile(filepath=OUT)

# 미리보기 (앞 3/4, 옆, 위) — EEVEE
sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 960, 640
w = bpy.data.worlds.new("w"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.8, 0.82, 0.85, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
sc.world = w
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 35
target = Vector((0, 0, 8))
for tag, pos in (("front", (-35, -70, 30)), ("side", (75, 0, 22)), ("back", (30, 70, 30))):
    cam.location = pos
    d = target - Vector(pos); cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = f"{PREV}_{tag}.png"
    bpy.ops.render.render(write_still=True)
print("DONE")
