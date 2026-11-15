"""Draws the VoxDeck mark (five level-meter bars forming a V) and writes src/Assets/app.ico + logo256.png.
The app draws the same shape as vectors at run time (src/Views/LogoMark.cs), in the colours of the active theme."""
from PIL import Image, ImageDraw

X = [0.2, 0.35, 0.5, 0.65, 0.8]
TOP = [0.215, 0.335, 0.455, 0.335, 0.215]
BAR_W, BAR_H, CORNER = 0.104, 0.33, 0.225


def mark(size, back=(198, 241, 53, 255), bars=(17, 17, 17, 255), ss=8):
    s = size * ss
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s * CORNER, fill=back)
    w = s * BAR_W
    for x, t in zip(X, TOP):
        d.rounded_rectangle([s * x - w / 2, s * t, s * x + w / 2, s * (t + BAR_H)], radius=w / 2, fill=bars)
    return im.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    imgs = [mark(s) for s in sizes]
    imgs[-1].save("src/Assets/app.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    mark(256).save("src/Assets/logo256.png")
    mark(512).save("docs/icon.png")
