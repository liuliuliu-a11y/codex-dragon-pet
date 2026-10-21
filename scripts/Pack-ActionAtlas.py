"""Register the generated 24 poses on an exact 4x6 runtime grid, preserving RGBA artwork."""
import argparse
import json
import statistics
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    source = Image.open(args.source).convert("RGBA")
    if source.size != (1024, 1536):
        raise ValueError("Expected the inspected 1024x1536 generated atlas")
    alpha = source.getchannel("A")
    # The generator uses visually even spacing, not exact cells. Cut inside observed clear gaps.
    columns = [0, 282, 526, 774, 1024]
    frames, regions = [], []
    for column in range(4):
        left, right = columns[column : column + 2]
        rows = [0]
        for expected in (256, 512, 768, 1024, 1280):
            count, cut = min(
                (sum(pixel > 128 for pixel in alpha.crop((left, y, right, y + 1)).tobytes()), y)
                for y in range(expected - 28, expected + 28)
            )
            if count:
                raise ValueError(f"No transparent row gap at column {column}, row {expected}")
            rows.append(cut)
        rows.append(source.height)
        for row in range(6):
            region = (left, rows[row], right, rows[row + 1])
            frame = source.crop(region)
            bounds = frame.getchannel("A").point(lambda pixel: 255 if pixel > 128 else 0).getbbox()
            if bounds is None:
                raise ValueError(f"Empty sprite at {row},{column}")
            bounds = (max(0, bounds[0] - 2), max(0, bounds[1] - 2), min(frame.width, bounds[2] + 2), min(frame.height, bounds[3] + 2))
            frames.append((row * 4 + column, frame.crop(bounds)))
            regions.append({"frame": row * 4 + column, "source_region": region, "bounds": bounds})
    frames.sort()
    scale = 234 / statistics.median(frame.height for _, frame in frames[:8])
    result = Image.new("RGBA", (1024, 1536))
    for index, frame in frames:
        size = (round(frame.width * scale), round(frame.height * scale))
        frame = frame.resize(size, Image.Resampling.LANCZOS)
        if frame.width > 250 or frame.height > 250:
            raise ValueError(f"Sprite {index} exceeds its cell")
        x = index % 4 * 256 + (256 - frame.width) // 2
        y = index // 4 * 256 + 249 - frame.height
        result.alpha_composite(frame, (x, y))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    result.save(args.output)
    bounds = []
    for index in range(24):
        cell = result.crop((index % 4 * 256, index // 4 * 256, (index % 4 + 1) * 256, (index // 4 + 1) * 256))
        bounds.append(cell.getchannel("A").getbbox())
        if any(cell.getchannel("A").crop(edge).getbbox() for edge in ((0, 0, 256, 1), (0, 255, 256, 256), (0, 0, 1, 256), (255, 0, 256, 256))):
            raise ValueError(f"Sprite {index} touches an atlas boundary")
    args.report.write_text(json.dumps({"source_size": source.size, "runtime_size": result.size, "scale": scale, "regions": regions, "runtime_bounds": bounds}, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
