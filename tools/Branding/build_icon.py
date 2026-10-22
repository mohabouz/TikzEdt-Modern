"""Package the generated logo into Windows ICO resolutions (no artwork edits)."""
import argparse
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("source", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
sizes = [(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)]
with Image.open(args.source) as source:
    if source.width != source.height:
        raise ValueError("The application logo must be square.")
    source.convert("RGBA").save(args.output, format="ICO", sizes=sizes)
with Image.open(args.output) as icon:
    assert icon.ico.sizes() == set(sizes)
print(f"Packaged {len(sizes)} icon resolutions: {args.output}")
