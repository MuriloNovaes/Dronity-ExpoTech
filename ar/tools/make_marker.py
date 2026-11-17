"""Gera o marcador próprio do ExpoTech para o AR.js.

Saídas (em ar/marker/):
- expotech-marker.png : imagem para imprimir ou mostrar numa tela (borda preta + desenho interno)
- expotech.patt       : padrão que o AR.js compara com a câmera

O .patt segue o formato do gerador oficial do AR.js (THREEx.ArPatternFile.encodeImage):
desenho interno reduzido a 16x16, 4 orientações (0°, 90°, 180°, 270° no sentido anti-horário),
e em cada uma os canais B, G, R, linha a linha. patternRatio = 0,5 (desenho = metade da borda).
Uso: python ar/tools/make_marker.py
"""

import os

from PIL import Image, ImageDraw

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "marker"))
os.makedirs(OUT, exist_ok=True)

# Desenho interno: assimétrico (o AR.js precisa saber a orientação) e com formas grandes,
# porque ele é comparado em 16x16.
S = 512
inner = Image.new("RGB", (S, S), "white")
d = ImageDraw.Draw(inner)
u = S // 8
d.rectangle([1 * u, 1 * u, 2 * u, 7 * u], fill="black")          # haste do "E"
d.rectangle([1 * u, 1 * u, 5 * u, 2 * u], fill="black")          # braço de cima
d.rectangle([1 * u, 3.5 * u, 4 * u, 4.5 * u], fill="black")      # braço do meio
d.rectangle([1 * u, 6 * u, 5 * u, 7 * u], fill="black")          # braço de baixo
d.ellipse([5.4 * u, 4.6 * u, 7.2 * u, 6.4 * u], fill="black")    # roda (marca de canto, quebra a simetria)
inner.save(os.path.join(OUT, "expotech-inner.png"))

# Marcador completo: margem branca + borda preta; o desenho ocupa 50% da borda (patternRatio 0,5).
FULL, MARGIN = 1000, 100
border = FULL - 2 * MARGIN
marker = Image.new("RGB", (FULL, FULL), "white")
ImageDraw.Draw(marker).rectangle([MARGIN, MARGIN, MARGIN + border - 1, MARGIN + border - 1], fill="black")
inner_px = border // 2
marker.paste(inner.resize((inner_px, inner_px), Image.LANCZOS), (MARGIN + border // 4, MARGIN + border // 4))
marker.save(os.path.join(OUT, "expotech-marker.png"))

# .patt: 4 orientações; canvas.rotate(-90°) do gerador oficial = giro anti-horário = PIL rotate(+90).
small = inner.resize((16, 16), Image.BILINEAR)
blocks = []
for k in range(4):
    img = small.rotate(90 * k)
    px = img.load()
    lines = []
    for channel in (2, 1, 0):  # B, G, R
        for y in range(16):
            lines.append(" ".join(f"{px[x, y][channel]:3d}" for x in range(16)))
    blocks.append("\n".join(lines))
with open(os.path.join(OUT, "expotech.patt"), "w", newline="\n") as f:
    f.write("\n\n".join(blocks) + "\n")
print("marcador gerado em", OUT)
