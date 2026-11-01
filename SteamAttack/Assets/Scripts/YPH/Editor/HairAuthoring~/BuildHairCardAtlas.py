"""Hair Cards의 알파와 표면 데이터 아틀라스를 만든다."""
from pathlib import Path
import numpy as np
from PIL import Image
import argparse

assets = Path(__file__).resolve().parents[4]
parser = argparse.ArgumentParser(description='Hair Cards용 두 Linear 데이터 PNG를 제작합니다.')
parser.add_argument('--output', type=Path, default=assets.parent / 'Temp/HairGroomBuild')
root = parser.parse_args().output.resolve()
root.mkdir(parents=True, exist_ok=True)
width, height = 2048, 1024
opacity = np.zeros((height, width), np.float32)
brightness = np.zeros_like(opacity)
rng = np.random.default_rng(3971)
y = np.linspace(0, 1, height)[:, None]
for column in range(8):
    x = np.linspace(0, 1, width // 8)[None, :]
    part = np.zeros((height, width // 8), np.float32)
    light = np.zeros_like(part)
    for strand in range(8 if column % 3 == 0 else 3):
        center = float(rng.uniform(.10, .90))
        start, end = rng.uniform(.01, .10), rng.uniform(.86, .99)
        bend = rng.uniform(-.08, .08) * np.sin(y * np.pi)
        bend += rng.uniform(.002, .012) * np.sin(y * rng.uniform(3, 8) + rng.uniform(0, 6.3))
        taper = np.clip((y - start) * 12, 0, 1) * np.clip((end - y) * 14, 0, 1)
        radius = rng.uniform(.018, .035) * np.maximum(taper, .03)
        coverage = np.clip(.5 + (radius - np.abs(x - center - bend)) * 256, 0, 1) * (taper > 0)
        strength = rng.uniform(.35, .9) * (.85 + .15 * np.sin(y * rng.uniform(10, 30) + strand))
        part = np.maximum(part, coverage)
        light = np.maximum(light, coverage * strength)
    opacity[:, column * 256:(column + 1) * 256] = part
    brightness[:, column * 256:(column + 1) * 256] = light
surface = np.empty((height, width, 4), np.float32)
dx = np.gradient(brightness, axis=1)
surface[:, :, 0] = np.clip(.5 - dx * .22, .1, .9)
surface[:, :, 1] = .5
surface[:, :, 2] = .15 + .24 * np.maximum(brightness, .15)
surface[:, :, 3] = .3 + .55 * brightness
base = np.ones_like(surface)
base[:, :, :3] = .2
base[:, :, 3] = opacity
Image.fromarray(np.uint8(np.rint(base * 255))).save(root / 'groom_card_opacity.png')
Image.fromarray(np.uint8(np.rint(surface * 255))).save(root / 'groom_card_surface.png')

print('카드용 표면·알파 아틀라스 저장 완료')
