"""Recreate the repository-owned geometric icon (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1] / "src/AiInput.App/Assets"
root.mkdir(parents=True, exist_ok=True)
scale = 4
canvas = Image.new("RGBA", (256*scale, 256*scale))
d = ImageDraw.Draw(canvas)
def box(bounds, radius, color):
    d.rounded_rectangle(tuple(int(x*scale) for x in bounds), radius*scale, fill=color)
box((8, 8, 248, 248), 58, "#253D69")
box((54, 68, 78, 190), 12, "#F4FAFF")
box((50, 60, 110, 80), 10, "#F4FAFF")
box((50, 180, 110, 200), 10, "#F4FAFF")
box((120, 100, 202, 118), 9, "#70E0D2")
box((120, 140, 182, 158), 9, "#F4FAFF")
d.ellipse((177*scale, 50*scale, 209*scale, 82*scale), fill="#70E0D2")
icon = canvas.resize((256, 256), Image.Resampling.LANCZOS)
icon.save(root / "App.ico", sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
icon.save(root / "App.png")
