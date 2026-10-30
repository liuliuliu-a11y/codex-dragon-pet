"""Register generated atlases and bake alpha-aware, motion-compensated 30 Hz clips.

Only the build step needs numpy/OpenCV/Pillow. The shipped pet uses WPF only.
Never writes to source artwork or a pre-existing output directory.
"""
import argparse
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

FORMS = ("low", "medium", "high", "xhigh", "ultra", "astra")
SIZE = 320


def register(path):
    atlas = np.array(Image.open(path).convert("RGBA"))
    h, w = atlas.shape[:2]
    if np.min(atlas[:, :, 3]) > 0:
        raise ValueError(f"Atlas must have genuine transparency: {path}")
    cells, boxes = [], []
    for n in range(16):
        cell = atlas[(n // 4) * h // 4:(n // 4 + 1) * h // 4,
                     (n % 4) * w // 4:(n % 4 + 1) * w // 4].copy()
        count, labels, stats, _ = cv2.connectedComponentsWithStats((cell[:, :, 3] > 40).astype(np.uint8), 8)
        if count < 2:
            raise ValueError(f"Empty atlas cell {n}: {path}")
        principal = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
        mask = (labels == principal).astype(np.uint8)
        # Retain antialiasing and attached ornaments; reject neighbouring-cell specks.
        mask = cv2.dilate(mask, np.ones((5, 5), np.uint8))
        cell *= mask[:, :, None]
        yy, xx = np.where(cell[:, :, 3] > 40)
        boxes.append((max(0, xx.min()-2), max(0, yy.min()-2), min(cell.shape[1], xx.max()+3), min(cell.shape[0], yy.max()+3)))
        cells.append(cell)
    # One scale for the whole form, independent of pose. Foot registration never
    # changes scale from frame to frame; sleep can become shorter naturally.
    scale = min(SIZE * .86 / max(b[3]-b[1] for b in boxes),
                SIZE * .94 / max(b[2]-b[0] for b in boxes))
    result = []
    for cell, (x0, y0, x1, y1) in zip(cells, boxes):
        crop = cv2.resize(cell[y0:y1, x0:x1], (max(1, round((x1-x0)*scale)), max(1, round((y1-y0)*scale))), interpolation=cv2.INTER_AREA)
        target = np.zeros((SIZE, SIZE, 4), np.uint8)
        x = (SIZE-crop.shape[1])//2
        y = round(SIZE*.94)-crop.shape[0]
        target[y:y+crop.shape[0], x:x+crop.shape[1]] = crop
        result.append(target)
    return result


def premultiply(frame):
    data = frame.astype(np.float32) / 255
    data[:, :, :3] *= data[:, :, 3:4]
    return data


def flow_pair(a, b):
    # White compositing makes transparent RGB irrelevant to motion estimation.
    def gray(p):
        rgb = p[:, :, :3] + (1-p[:, :, 3:4])
        return cv2.cvtColor(np.uint8(np.clip(rgb*255, 0, 255)), cv2.COLOR_RGB2GRAY)
    ag, bg = gray(a), gray(b)
    engine = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
    fwd = engine.calc(ag, bg, None)
    back = engine.calc(bg, ag, None)
    return fwd, back


def interpolate(a, b, flows, t):
    if t <= 0:
        return a
    if t >= 1:
        return b
    fwd, back = flows
    yy, xx = np.mgrid[:SIZE, :SIZE].astype(np.float32)
    # Two fixed-point inverse-warp iterations align both drawings at the same
    # intermediate pose before opacity blending, instead of double-image fades.
    def warp(img, flow, fraction):
        mx, my = xx.copy(), yy.copy()
        for _ in range(3):
            sampled = cv2.remap(flow, mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
            mx = xx - sampled[:, :, 0]*fraction
            my = yy - sampled[:, :, 1]*fraction
        return cv2.remap(img, mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
    return warp(a, fwd, t)*(1-t) + warp(b, back, 1-t)*t


def encode(path, data):
    alpha = data[:, :, 3:4]
    rgba = np.concatenate((np.divide(data[:, :, :3], alpha, out=np.zeros_like(data[:, :, :3]), where=alpha > .001), alpha), axis=2)
    Image.fromarray(np.uint8(np.clip(rgba*255+.5, 0, 255))).save(path, compress_level=2)


def microcycle(frame, key, phase):
    """Small local secondary motion during held poses, with a closed loop seam."""
    yy, xx = np.mgrid[:SIZE, :SIZE].astype(np.float32)
    u, v = xx / SIZE, yy / SIZE
    wave = np.sin(phase * 2*np.pi)
    def spot(cx, cy, sx, sy):
        return np.exp(-((u-cx)/sx)**2-((v-cy)/sy)**2)
    tail = spot(.82, .73, .23, .25)
    hair = spot(.2, .60, .16, .34) + spot(.79, .56, .16, .3)
    dx = (tail*1.5 + hair*.55)*wave
    dy = np.zeros_like(dx)
    if key.startswith('Work:'):
        hands = spot(.44, .63, .08, .07) + spot(.60, .63, .08, .07)
        dy += hands * np.sin(phase*6*np.pi) * 1.7
    elif key.startswith('Sleep:'):
        dy += spot(.5, .77, .27, .18) * (1-np.cos(phase*2*np.pi)) * .65
    elif key.startswith('Raised'):
        dy += spot(.5, .86, .25, .11) * wave * 1.7
    else:
        dx += spot(.5, .3, .2, .2) * wave * .45
    return cv2.remap(frame, (xx-dx).astype(np.float32), (yy-dy).astype(np.float32), cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)


def specifications():
    # Each edge has a real time duration. Native A/B/C playback and callbacks are
    # preserved; looping clips have an explicitly matching first/last pose.
    return {
        "Default:idle:Single": [(0, 2600), (1, 110), (0, 110), (0, 2400)],
        "Touch_Head:pet:A_Start": [(0, 30), (2, 300)],
        "Touch_Head:pet:B_Loop": [(2, 350), (1, 170), (2, 170)],
        "Touch_Head:pet:C_End": [(2, 30), (0, 300)],
        "Touch_Body:curious:A_Start": [(0, 30), (4, 280)],
        "Touch_Body:curious:B_Loop": [(4, 180), (3, 280), (4, 280)],
        "Touch_Body:curious:C_End": [(4, 30), (0, 280)],
        "Sleep:nap:A_Start": [(0, 30), (13, 480), (14, 480)],
        "Sleep:nap:B_Loop": [(14, 2200)],
        "Sleep:nap:C_End": [(14, 30), (13, 480), (0, 480)],
        "Raised_Static:dangle:A_Start": [(0, 30), (11, 260)],
        "Raised_Static:dangle:B_Loop": [(11, 700)],
        "Raised_Static:dangle:C_End": [(11, 30), (12, 240), (0, 240)],
        "Raised_Dynamic:dangle:Single": [(11, 650)],
        "Move:walk.right:A_Start": [(0, 30), (9, 220)],
        "Move:walk.right:B_Loop": [(9, 30), (10, 330), (9, 330)],
        "Move:walk.right:C_End": [(9, 30), (0, 240)],
        "Work:typing:A_Start": [(0, 30), (15, 420)],
        "Work:typing:B_Loop": [(15, 1300)],
        "Work:typing:C_End": [(15, 30), (0, 420)],
        "Idel:thinking:A_Start": [(0, 30), (4, 260)],
        "Idel:thinking:B_Loop": [(4, 600)],
        "Idel:thinking:C_End": [(4, 30), (0, 260)],
        "Say:speaking:A_Start": [(0, 30), (2, 250)],
        "Say:speaking:B_Loop": [(2, 180), (0, 180), (2, 180)],
        "Say:speaking:C_End": [(2, 30), (0, 250)],
        "StartUP:hello:Single": [(0, 30), (4, 220), (3, 260), (4, 260), (3, 260), (0, 320)],
        "Common:celebrate:Single": [(0, 30), (4, 220), (3, 260), (4, 260), (3, 260), (0, 320)],
        "Common:dragon-eat:Single": [(0, 30), (7, 380), (8, 420), (8, 450), (0, 320)],
        "Common:dragon-drink:Single": [(0, 30), (5, 380), (6, 440), (6, 750), (5, 440), (0, 380)],
    }


def bake(sheet, output):
    frames = [premultiply(f) for f in register(sheet)]
    output.mkdir(parents=True, exist_ok=False)
    pairs = {}
    manifest = {}
    for key, sequence in specifications().items():
        folder = output / key.replace(":", "_")
        folder.mkdir()
        paths, times = [], []
        last = sequence[0][0]
        def append(data, duration):
            filename = f"{len(paths):04}.png"
            encode(folder/filename, data)
            paths.append(str((folder/filename).relative_to(output)).replace("\\", "/"))
            times.append(duration)
        if len(sequence) == 1:
            duration = sequence[0][1]
            steps = max(2, round(duration*30/1000))
            for step in range(steps):
                append(microcycle(frames[last], key, step/steps), duration/steps)
            manifest[key] = {"frames": paths, "milliseconds": times}
            continue
        append(frames[last], sequence[0][1])
        for nxt, duration in sequence[1:]:
            if last == nxt:
                append(frames[nxt], duration)
            else:
                pair = (last, nxt)
                if pair not in pairs:
                    pairs[pair] = flow_pair(frames[last], frames[nxt])
                steps = max(2, round(duration*30/1000))
                for step in range(1, steps+1):
                    t = step/steps
                    # Smooth start and stop in pose space.
                    t = t*t*(3-2*t)
                    append(interpolate(frames[last], frames[nxt], pairs[pair], t), duration/steps)
            last = nxt
        manifest[key] = {"frames": paths, "milliseconds": times}
        if ":walk.right:" in key:
            left = key.replace("walk.right", "walk.left")
            manifest[left] = dict(manifest[key], flip=True)
    (output/"clips.json").write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({"form": output.name, "clips": len(manifest), "frames": sum(len(c["frames"]) for c in manifest.values())}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--sheets", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--form", choices=FORMS)
    args = parser.parse_args()
    for form in [args.form] if args.form else FORMS:
        bake(args.sheets/(form+".png"), args.output/form)
