"""Generate LlamaLink icon - a llama silhouette with a chain link motif."""
from PIL import Image, ImageDraw, ImageFont
import math, os

SIZES = [16, 24, 32, 48, 64, 128, 256, 512]

# Catppuccin Mocha colors
BG = (30, 30, 46)         # base
BLUE = (137, 180, 250)    # blue
LAVENDER = (180, 190, 254) # lavender
GREEN = (166, 227, 161)   # green
CRUST = (17, 17, 27)      # crust
SURFACE0 = (49, 50, 68)   # surface0
MAUVE = (203, 166, 247)   # mauve

def draw_icon(size):
    """Draw LlamaLink icon at given size."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    s = size  # shorthand
    pad = s * 0.04

    # Background circle with subtle gradient feel
    draw.ellipse([pad, pad, s - pad, s - pad], fill=BG, outline=SURFACE0, width=max(1, int(s * 0.02)))

    # Inner glow ring
    ring_pad = s * 0.08
    draw.ellipse([ring_pad, ring_pad, s - ring_pad, s - ring_pad],
                 outline=(*BLUE, 40), width=max(1, int(s * 0.015)))

    cx, cy = s / 2, s / 2

    # ── Draw stylized llama head silhouette ──
    # Scale factor
    f = s / 512.0

    # Llama head - simplified geometric shape
    # Neck/body base
    body_pts = [
        (cx - 80*f, cy + 120*f),   # bottom left
        (cx - 90*f, cy + 40*f),    # left side
        (cx - 70*f, cy - 30*f),    # left cheek
        (cx - 55*f, cy - 80*f),    # left jaw up
        (cx - 40*f, cy - 110*f),   # forehead left
        (cx - 30*f, cy - 130*f),   # top of head left
        # Left ear
        (cx - 55*f, cy - 165*f),   # ear base left
        (cx - 65*f, cy - 200*f),   # ear tip left
        (cx - 40*f, cy - 170*f),   # ear inner left
        # Top of head
        (cx - 15*f, cy - 140*f),   # between ears
        (cx + 15*f, cy - 140*f),
        # Right ear
        (cx + 40*f, cy - 170*f),   # ear inner right
        (cx + 65*f, cy - 200*f),   # ear tip right
        (cx + 55*f, cy - 165*f),   # ear base right
        # Right side down
        (cx + 30*f, cy - 130*f),
        (cx + 40*f, cy - 110*f),
        (cx + 55*f, cy - 80*f),    # right jaw
        (cx + 50*f, cy - 40*f),    # snout
        (cx + 60*f, cy - 20*f),    # nose tip
        (cx + 45*f, cy + 10*f),    # under chin
        (cx + 50*f, cy + 60*f),    # right neck
        (cx + 80*f, cy + 120*f),   # bottom right
    ]

    # Draw llama silhouette
    draw.polygon(body_pts, fill=LAVENDER)

    # Eye
    eye_x = cx - 10*f
    eye_y = cy - 95*f
    eye_r = 8*f
    draw.ellipse([eye_x - eye_r, eye_y - eye_r, eye_x + eye_r, eye_y + eye_r], fill=CRUST)
    # Eye highlight
    hl_r = 3*f
    draw.ellipse([eye_x - eye_r + 3*f, eye_y - eye_r + 2*f,
                  eye_x - eye_r + 3*f + hl_r, eye_y - eye_r + 2*f + hl_r], fill=BLUE)

    # ── Chain link symbol (bottom) ──
    link_y = cy + 100*f
    link_w = 42*f
    link_h = 22*f
    link_thick = max(2, int(5*f))

    # Left link
    lx = cx - 22*f
    draw.rounded_rectangle(
        [lx - link_w/2, link_y - link_h/2, lx + link_w/2, link_y + link_h/2],
        radius=link_h/2, outline=BLUE, width=link_thick
    )
    # Right link (overlapping)
    rx = cx + 22*f
    draw.rounded_rectangle(
        [rx - link_w/2, link_y - link_h/2, rx + link_w/2, link_y + link_h/2],
        radius=link_h/2, outline=GREEN, width=link_thick
    )

    return img


def main():
    os.makedirs("assets", exist_ok=True)

    # Generate all sizes
    images = []
    for size in SIZES:
        img = draw_icon(size)
        images.append(img)
        if size == 512:
            img.save("assets/icon_512.png")
        if size == 128:
            img.save("assets/icon_128.png")

    # Save as .ico (Windows icon with multiple sizes)
    ico_sizes = [s for s in SIZES if s <= 256]
    ico_images = [draw_icon(s) for s in ico_sizes]
    ico_images[0].save(
        "assets/llamalink.ico",
        format="ICO",
        sizes=[(s, s) for s in ico_sizes],
        append_images=ico_images[1:],
    )

    print(f"Generated assets/llamalink.ico ({len(ico_sizes)} sizes)")
    print(f"Generated assets/icon_512.png")
    print(f"Generated assets/icon_128.png")


if __name__ == "__main__":
    main()
