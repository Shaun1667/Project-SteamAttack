"""Blender의 BVH와 Curve로 다발 홈, 표면 카드, 잔머리를 제작한다."""
import bpy
import math
import struct
import time
import argparse
import sys
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

ASSETS = Path(__file__).resolve().parents[4]
SOURCE = ASSETS / 'Arts/Models/YPH/FinalPlayer3/HairAuthoring~'
parser = argparse.ArgumentParser(description='머리 다발과 Hair Cards의 Blender 원본 및 리깅 연결 데이터를 제작합니다.')
parser.add_argument('--output', type=Path, default=ASSETS.parent / 'Temp/HairGroomBuild')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
ROOT = args.output.resolve()
ROOT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
with (SOURCE / 'OriginalHairMesh.bin').open('rb') as stream:
    count, index_count = struct.unpack('<II', stream.read(8))
    data = np.fromfile(stream, '<f4', count=count * 12).reshape(-1, 12)
    faces = np.fromfile(stream, '<u4', count=index_count).reshape(-1, 3)
positions = [Vector(p) for p in data[:, :3]]
normals = [Vector(n) for n in data[:, 3:6]]
tangents = [Vector(t) for t in data[:, 6:9]]
bvh = BVHTree.FromPolygons(positions, faces.tolist(), all_triangles=True)
image = bpy.data.images.load(str(ASSETS / 'Arts/Models/YPH/FinalPlayer3/Textures/YPH_HairReferenceFlow.png'))
image.colorspace_settings.name = 'Non-Color'
pixels = np.empty(image.size[0] * image.size[1] * 4, np.float32)
image.pixels.foreach_get(pixels)
pixels = pixels.reshape(image.size[1], image.size[0], 4)


def evaluate(point):
    """원본 삼각형에 투영해 부드러운 법선과 두 배 각도의 머리결을 읽는다."""
    loc, _, fi, distance = bvh.find_nearest(point)
    if fi is None:
        return None
    ids = faces[fi]
    a, b, c = (positions[int(i)] for i in ids)
    u, v, q = b - a, c - a, loc - a
    aa, ab, bb = u.dot(u), u.dot(v), v.dot(v)
    den = aa * bb - ab * ab
    if abs(den) < 1e-18:
        return None
    wb = (bb * q.dot(u) - ab * q.dot(v)) / den
    wc = (aa * q.dot(v) - ab * q.dot(u)) / den
    wa = 1 - wb - wc
    n = sum((normals[int(i)] * w for i, w in zip(ids, (wa, wb, wc))), Vector()).normalized()
    t = sum((tangents[int(i)] * w for i, w in zip(ids, (wa, wb, wc))), Vector())
    t = (t - n * t.dot(n)).normalized()
    handedness = -1 if sum(float(data[i, 9]) * w for i, w in zip(ids, (wa, wb, wc))) < 0 else 1
    bitangent = n.cross(t) * handedness
    uv = sum((data[i, 10:12] * w for i, w in zip(ids, (wa, wb, wc))), np.zeros(2))
    x, y = np.clip(uv, 0, 1) * np.array((pixels.shape[1] - 1, pixels.shape[0] - 1))
    x0, y0 = int(x), int(y)
    x1, y1 = min(x0 + 1, pixels.shape[1] - 1), min(y0 + 1, pixels.shape[0] - 1)
    fx, fy = x - x0, y - y0
    rg = (pixels[y0, x0, :2] * (1 - fx) + pixels[y0, x1, :2] * fx) * (1 - fy)
    rg += (pixels[y1, x0, :2] * (1 - fx) + pixels[y1, x1, :2] * fx) * fy
    doubled = rg * 2 - 1
    theta = .5 * math.atan2(float(doubled[1]), float(doubled[0]))
    fiber = t * math.cos(theta) + bitangent * math.sin(theta)
    return loc, n, fiber.normalized(), fi, wa, wb, uv, distance


def trace(seed, length, step=.003):
    """곡면의 방향장을 따라 양쪽으로 연결된 가이드 곡선을 만든다."""
    initial = evaluate(seed)
    if initial is None:
        return []
    halves = []
    for sign in (-1, 1):
        chain, previous, point = [], initial[2] * sign, initial[0]
        for _ in range(int(length / step)):
            sample = evaluate(point)
            if sample is None or sample[7] > step * 1.8:
                break
            loc, n, fiber = sample[:3]
            if fiber.dot(previous) < 0:
                fiber = -fiber
            if chain and fiber.dot(previous) < .3:
                break
            fiber = (fiber * .5 + previous * .5).normalized()
            if chain and (loc - chain[-1][0]).length < step * .3:
                break
            chain.append(sample)
            previous, point = fiber, loc + fiber * step
        halves.append(chain)
    return halves[0][:0:-1] + halves[1]


start = time.monotonic()
guides = []
for x in np.linspace(-.225, .225, 11):
    path = trace(Vector((float(x), .45, 3.55)), .175)
    if len(path) > 12:
        guides.append(path)
for sign in (-1, 1):
    for y in (-.025, .09, .21):
        path = trace(Vector((sign * .34, y, 3.62)), .13)
        if len(path) > 12:
            guides.append(path)

tree = KDTree(sum(len(path) for path in guides))
guide_weights = []
guide_segments = []
for path in guides:
    for j, sample in enumerate(path):
        f = j / (len(path) - 1)
        guide_weights.append(min(1., f * 7, (1 - f) * 7))
        guide_segments.append((path[max(0, j - 1)][0], path[min(len(path) - 1, j + 1)][0]))
        tree.insert(sample[0], len(guide_weights) - 1)
tree.balance()


def height(point):
    """큰 다발의 경계에만 완만한 홈을 만들고 갓과 얼굴 근처는 줄인다."""
    best = 0.
    for _, index, _ in tree.find_n(point, 4):
        a, b = guide_segments[index]
        direction = b - a
        fraction = max(0., min(1., (point - a).dot(direction) / max(direction.length_squared, 1e-12)))
        distance = (point - a - direction * fraction).length
        best = max(best, math.exp(-.5 * (distance / .0095) ** 2) * guide_weights[index])
    top_fade = 1 - max(0., min(1., (point.z - 3.725) / .045))
    face_fade = .35 if point.y < .035 and point.z < 3.58 else 1.
    return -.0075 * best * top_fade * face_fade


def deformed_normal(point, n, t):
    """홈의 기울기를 기존 부드러운 법선에 더해 가짜 평면 반사를 피한다."""
    t = (t - n * t.dot(n)).normalized()
    b = n.cross(t).normalized()
    epsilon = .0007
    dt = (height(point + t * epsilon) - height(point - t * epsilon)) / (2 * epsilon)
    db = (height(point + b * epsilon) - height(point - b * epsilon)) / (2 * epsilon)
    return (n - t * dt * .45 - b * db * .45).normalized()


def write_mesh(name, vertices, triangles):
    """Unity에서 같은 뼈 가중치를 복원할 수 있게 원본 삼각형 좌표도 저장한다."""
    with (ROOT / name).open('wb') as stream:
        stream.write(struct.pack('<II', len(vertices), len(triangles) * 3))
        for p, n, uv, source in vertices:
            stream.write(struct.pack('<8fi2f', *p, *n, *uv, int(source[0]), float(source[1]), float(source[2])))
        stream.write(np.asarray(triangles, dtype='<u4').tobytes())


base_vertices, base_triangles = [], []
weights = np.array(((1, 0, 0), (0, 1, 0), (0, 0, 1), (.5, .5, 0), (0, .5, .5), (.5, 0, .5)))
split = ((0, 3, 5), (3, 1, 4), (5, 4, 2), (3, 4, 5))
for fi, ids in enumerate(faces):
    begin = len(base_vertices)
    for bary in weights:
        p = Vector(bary @ data[ids, :3])
        n = Vector(bary @ data[ids, 3:6]).normalized()
        t = Vector(bary @ data[ids, 6:9])
        uv = bary @ data[ids, 10:12]
        base_vertices.append((p + n * height(p), deformed_normal(p, n, t), uv, (fi, bary[0], bary[1])))
    base_triangles.extend(tuple(begin + i for i in tri) for tri in split)
write_mesh('groom_base.bin', base_vertices, base_triangles)

rng = np.random.default_rng(20261031)
areas = np.array([((positions[b] - positions[a]).cross(positions[c] - positions[a])).length * .5 for a, b, c in faces])
areas /= areas.sum()
paths = []
for fi in rng.choice(len(faces), 155, p=areas):
    a, b = rng.random(2)
    bary = (1 - math.sqrt(a), math.sqrt(a) * (1 - b), math.sqrt(a) * b)
    seed = sum((positions[int(i)] * float(w) for i, w in zip(faces[fi], bary)), Vector())
    # 얼굴을 가리는 긴 새 다발과 갓을 뚫는 윗부분은 제외한다.
    if seed.z > 3.745 or (seed.y < -.07 and seed.z < 3.54):
        continue
    path = trace(seed, float(rng.uniform(.025, .065)), .003)
    if len(path) > 9:
        paths.append(path)

surface_path_count = len(paths)
# 여러 시점의 외곽에서 읽히는 잔머리를 추가한다. 촘촘한 전체 덮개는 피한다.
for z in (3.48, 3.60, 3.70):
    for angle in np.linspace(0, 2 * math.pi, 12, endpoint=False):
        seed = Vector((.39 * math.cos(angle), .08 + .40 * math.sin(angle), z))
        sample = evaluate(seed)
        if sample[0].z > 3.745 or (sample[0].y < -.07 and sample[0].z < 3.54):
            continue
        path = trace(sample[0], float(rng.uniform(.025, .045)), .003)
        if len(path) > 9:
            paths.append(path)

card_vertices, card_triangles = [], []
curve_data = bpy.data.curves.new('HairGuides_Editable', 'CURVE')
curve_data.dimensions = '3D'
curve_data.bevel_depth = .00013
curve_data.bevel_resolution = 0

for ci, path in enumerate(paths):
    begin = len(card_vertices)
    flyaway = (ci >= surface_path_count or ci % 5 == 0) and not any(s[0].y < .05 and s[0].z < 3.62 for s in path)
    width = float(rng.uniform(.0030, .0055) if flyaway else rng.uniform(.006, .010))
    lift = float(rng.uniform(.004, .018) if flyaway else rng.uniform(.0015, .0030))
    column = (1, 2, 4, 5, 7)[ci % 5] if flyaway else (0, 3, 6)[ci % 3]
    spline = curve_data.splines.new('POLY')
    spline.points.add(len(path) - 1)
    for j, sample in enumerate(path):
        p, n, _, _, _, _, _, _ = sample
        tangent = (path[min(j + 1, len(path) - 1)][0] - path[max(0, j - 1)][0]).normalized()
        tangent = (tangent - n * tangent.dot(n)).normalized()
        across = tangent.cross(n).normalized()
        fraction = j / (len(path) - 1)
        arch = fraction * fraction * (3 - 2 * fraction) if flyaway else math.sin(math.pi * fraction) ** 1.5
        center = p + n * (height(p) + .0007 + lift * arch)
        spline.points[j].co = (*center, 1)
        for k in (-1, 0, 1):
            q = center + across * (k * width * .5) + n * ((1 - k * k) * width * .08)
            source = evaluate(q)
            nn = (n + across * (k * .12)).normalized()
            uv = ((column + (k + 1) * .5) / 8, .02 + .96 * fraction)
            card_vertices.append((q, nn, uv, (source[3], source[4], source[5])))
    for j in range(len(path) - 1):
        for side in range(2):
            a = begin + j * 3 + side
            b, c = a + 1, a + 3
            d = c + 1
            p, n = card_vertices[a][:2]
            winding = (card_vertices[c][0] - p).cross(card_vertices[b][0] - p).dot(n)
            card_triangles.extend(((a, c, b), (b, c, d)) if winding > 0 else ((a, b, c), (b, d, c)))
write_mesh('groom_cards.bin', card_vertices, card_triangles)

mesh = bpy.data.meshes.new('HairClumps_Refined')
mesh.from_pydata([v[0] for v in base_vertices], [], base_triangles)
mesh.update()
layer = mesh.uv_layers.new(name='OriginalUV')
for poly in mesh.polygons:
    poly.use_smooth = True
    for li in poly.loop_indices:
        layer.data[li].uv = base_vertices[mesh.loops[li].vertex_index][2]
mesh.normals_split_custom_set_from_vertices([v[1] for v in base_vertices])
obj = bpy.data.objects.new('HairClumps_Refined', mesh)
bpy.context.collection.objects.link(obj)
mesh = bpy.data.meshes.new('HairCards_Refined')
mesh.from_pydata([v[0] for v in card_vertices], [], card_triangles)
mesh.update()
layer = mesh.uv_layers.new(name='CardUV')
for poly in mesh.polygons:
    poly.use_smooth = True
    for li in poly.loop_indices:
        layer.data[li].uv = card_vertices[mesh.loops[li].vertex_index][2]
mesh.normals_split_custom_set_from_vertices([v[1] for v in card_vertices])
obj = bpy.data.objects.new('HairCards_Refined', mesh)
bpy.context.collection.objects.link(obj)
obj = bpy.data.objects.new('HairGuides_Editable', curve_data)
bpy.context.collection.objects.link(obj)
obj.hide_render = True
obj.hide_set(True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'HairGroomRefined.blend'))
print('GROOM', 'guides', len(guides), 'base vertices', len(base_vertices), 'base triangles', len(base_triangles), 'cards', len(paths), 'card vertices', len(card_vertices), 'card triangles', len(card_triangles), 'seconds', round(time.monotonic() - start, 1), flush=True)
