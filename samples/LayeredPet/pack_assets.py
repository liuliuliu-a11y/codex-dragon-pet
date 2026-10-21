"""Register generated rig parts and export technical masks, preserving original face pixels."""
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT.parent.parent / "assets" / "layered"
SPRITES = ROOT.parent.parent / "assets" / "dragon-sprites-clean.png"


def polygon_mask(points, blur=0):
    mask = Image.new("L", (512, 512))
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return mask.filter(ImageFilter.GaussianBlur(blur)) if blur else mask


def save(im, name):
    # Clear invisible RGB for consistent previews; retain the input alpha and visible pixels.
    empty = Image.new("RGBA", im.size)
    empty.alpha_composite(im)
    empty.save(ASSETS / name)


def main():
    ASSETS.mkdir(exist_ok=True)
    sheet = Image.open(SPRITES).convert("RGBA")
    original = sheet.crop((0, 0, sheet.width // 3, sheet.height // 2))
    generated = Image.open(ROOT / "source/rig-parts-generated.png").convert("RGBA")
    closed = Image.open(ROOT / "source/blink-generated.png").convert("RGBA")
    if generated.size != (1254, 1254) or closed.size != (1254, 1254):
        raise ValueError("Registration is specific to the inspected generated files")

    arm_mask = polygon_mask([
        (224, 281), (214, 286), (202, 296), (197, 317), (190, 335),
        (181, 356), (183, 373), (204, 364), (225, 351), (240, 358),
        (261, 356), (279, 350), (298, 364), (321, 378), (324, 357),
        (325, 326), (324, 304), (318, 288), (303, 278), (292, 287),
        (291, 307), (285, 320), (264, 329), (249, 330), (230, 319),
        (224, 304),
    ], 1.0)
    # Align the reconstructed torso by its choker, waist and hips, then use only the occluded area.
    body_source = generated.crop((0, 0, 640, 640))
    registered = body_source.transform((512, 512), Image.Transform.AFFINE,
        (1 / .75, 0, -2.25 / .75, 0, 1 / .75, -25.25 / .75), Image.Resampling.BICUBIC)
    body = Image.composite(registered, original, arm_mask)
    old_arms = Image.new("RGBA", (512, 512))
    old_arms.paste(original, (0, 0), arm_mask)

    # Generated closed-eyelid texture is registered to the original face; the original head stays intact.
    blink_registered = closed.resize((512, 512), Image.Resampling.LANCZOS)
    blink_registered = blink_registered.transform((512, 512), Image.Transform.AFFINE,
        (1, 0, 1.5, 0, 1, 3), Image.Resampling.BICUBIC)
    eyes_mask = Image.new("L", (512, 512))
    eye_draw = ImageDraw.Draw(eyes_mask)
    eye_draw.polygon([(187, 204), (197, 193), (219, 189), (238, 198), (243, 213), (236, 230), (214, 234), (194, 225)], fill=255)
    eye_draw.polygon([(265, 179), (279, 165), (300, 165), (315, 176), (316, 192), (306, 207), (288, 215), (272, 205)], fill=255)
    eyes_mask = eyes_mask.filter(ImageFilter.GaussianBlur(1.5))
    eyelids = Image.new("RGBA", (512, 512))
    eyelids.paste(blink_registered, (0, 0), eyes_mask)

    save(original, "original.png")
    save(body, "body.png")
    save(old_arms, "rest-arms.png")
    save(eyelids.crop((180, 156, 324, 238)), "eyelids.png")
    save(generated.crop((815, 210, 1110, 630)), "left-arm.png")
    save(generated.crop((150, 780, 425, 1205)), "right-arm.png")
    cup = generated.crop((756, 807, 1121, 1090))
    bounds = cup.getchannel("A").point(lambda a: 255 if a > 20 else 0).getbbox()
    save(cup.crop(bounds), "cup.png")
    arm_mask.save(ASSETS / "arm-mask.png")

    face_box = (183, 165, 323, 250)
    if body.crop(face_box).tobytes() != original.crop(face_box).tobytes():
        raise AssertionError("The original face must remain byte-identical in the body texture")
    manifest = {
        "original_source": "assets/dragon-sprites-clean.png#idle",
        "source_sprite_sheet_sha256": hashlib.sha256(SPRITES.read_bytes()).hexdigest(),
        "original_sha256": hashlib.sha256((ASSETS / "original.png").read_bytes()).hexdigest(),
        "original_face_retained_pixel_exact": True,
        "face_region_xyxy": face_box,
        "new_artwork": "Generated torso repair, arm/sleeve/hand parts, porcelain cup and closed-eyelid texture",
        "animation": "Continuous mesh deformation and shoulder/elbow/wrist rig; no full-character pose slideshow",
        "generator": "Built-in image_gen; no local model downloads",
        "parts": [p.name for p in ASSETS.glob("*.png")],
    }
    (ASSETS / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    for name, im in [("body-check", body), ("idle-check", Image.alpha_composite(body, old_arms))]:
        bg = Image.new("RGBA", (512, 512), (242, 239, 234, 255))
        bg.alpha_composite(im)
        bg.convert("RGB").save(ROOT / "source" / (name + ".jpg"))
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
