"""Inspect the actual gdigrab recording; never manufacture intermediate frames."""
import json
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageChops

movie = Path(sys.argv[1])
report_path = Path(sys.argv[2])
probe = json.loads(subprocess.check_output([
    "ffprobe", "-v", "error", "-count_frames", "-select_streams", "v:0",
    "-show_entries", "stream=width,height,r_frame_rate,nb_read_frames,duration", "-of", "json", str(movie)
]))["streams"][0]
if float(probe["duration"]) < 15 or probe["r_frame_rate"] != "30/1":
    raise AssertionError("Incomplete native window recording")

# Examine the actively moving raise-cup interval, excluding captions and controls.
process = subprocess.Popen([
    "ffmpeg", "-v", "error", "-ss", "4.0", "-i", str(movie), "-t", "2.6",
    "-vf", "crop=380:420:110:100,scale=190:210", "-f", "rawvideo", "-pix_fmt", "gray", "-"
], stdout=subprocess.PIPE)
differences = []
previous = None
while True:
    frame = process.stdout.read(190 * 210)
    if not frame:
        break
    if len(frame) != 190 * 210:
        raise AssertionError("Truncated analysis frame")
    current = Image.frombytes("L", (190, 210), frame)
    if previous is not None:
        histogram = ImageChops.difference(current, previous).histogram()
        differences.append(sum(i * count for i, count in enumerate(histogram)) / (190 * 210))
    previous = current
if process.wait() != 0:
    raise AssertionError("Recording decode failed")
result = {
    "recording": str(movie), "source": "FFmpeg gdigrab of actual Layered Dragon Preview window",
    "video": probe,
    "moving_interval_seconds": [4.0, 6.6],
    "adjacent_frame_pairs": len(differences),
    "pairs_with_mean_pixel_change_above_0_015": sum(v > .015 for v in differences),
    "max_mean_pixel_change": max(differences),
    "note": "Decoded pixel change detects long held video frames; compression affects this measure. It is not a physical monitor refresh measurement.",
}
report_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
