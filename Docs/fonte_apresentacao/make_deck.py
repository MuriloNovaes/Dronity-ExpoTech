"""Gera ExpoTech_Apresentacao.pdf: slides 16:9 com roteiro de fala em cada página."""
import os
import sys

from reportlab.lib.colors import HexColor, white
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Frame, Paragraph, Table, TableStyle

OUT = sys.argv[1]
IMG = sys.argv[2]
W, H = 960, 540

pdfmetrics.registerFont(TTFont("Segoe", r"C:\Windows\Fonts\segoeui.ttf"))
pdfmetrics.registerFont(TTFont("Segoe-B", r"C:\Windows\Fonts\segoeuib.ttf"))
pdfmetrics.registerFont(TTFont("Segoe-L", r"C:\Windows\Fonts\segoeuil.ttf"))
pdfmetrics.registerFont(TTFont("Segoe-SB", r"C:\Windows\Fonts\seguisb.ttf"))
pdfmetrics.registerFontFamily("Segoe", normal="Segoe", bold="Segoe-B", italic="Segoe", boldItalic="Segoe-B")

INK = HexColor("#1D2226")
MUTED = HexColor("#5B6470")
BG = HexColor("#F5F6F4")
OLIVE = HexColor("#5B6B3A")
OLIVE_L = HexColor("#E6EADB")
LED = HexColor("#E0452B")
LINE = HexColor("#C9CEC4")
NOTE_BG = HexColor("#22282D")
NOTE_INK = HexColor("#E8EBEE")
BLUE = HexColor("#2F6FB3")
AMBER = HexColor("#D9A21B")

TOTAL = 14
TOP = 470          # base do título
CONTENT_TOP = 440  # topo da área de conteúdo
CONTENT_BOTTOM = 182

body = ParagraphStyle("body", fontName="Segoe", fontSize=13, leading=17.5, textColor=INK)
small = ParagraphStyle("small", parent=body, fontSize=11, leading=14.5)
bullet = ParagraphStyle("bullet", parent=body, leftIndent=14, bulletIndent=0, spaceAfter=5)
note = ParagraphStyle("note", fontName="Segoe", fontSize=10.6, leading=14.2, textColor=NOTE_INK)
cell = ParagraphStyle("cell", fontName="Segoe", fontSize=10.5, leading=13, textColor=INK)
cellb = ParagraphStyle("cellb", parent=cell, fontName="Segoe-B", textColor=white)


def page(c, n, title, subtitle=None):
    c.setFillColor(BG)
    c.rect(0, 0, W, H, stroke=0, fill=1)
    c.setFillColor(OLIVE)
    c.rect(0, H - 8, W, 8, stroke=0, fill=1)
    c.setFillColor(LED)
    c.rect(40, TOP + 36, 28, 4, stroke=0, fill=1)
    c.setFont("Segoe-B", 25)
    c.setFillColor(INK)
    c.drawString(40, TOP, title)
    if subtitle:
        c.setFont("Segoe", 13)
        c.setFillColor(MUTED)
        c.drawString(40, TOP - 20, subtitle)
    c.setFont("Segoe", 9.5)
    c.setFillColor(MUTED)
    c.drawRightString(W - 40, TOP + 36, f"ExpoTech · {n}/{TOTAL}")


def notes(c, text, tempo):
    c.setFillColor(NOTE_BG)
    c.roundRect(28, 14, W - 56, 156, 10, stroke=0, fill=1)
    c.setFillColor(LED)
    c.circle(48, 152, 4, stroke=0, fill=1)
    c.setFont("Segoe-B", 10)
    c.setFillColor(white)
    c.drawString(58, 148.5, "ROTEIRO DE FALA")
    c.setFont("Segoe", 10)
    c.setFillColor(HexColor("#AEB6BE"))
    c.drawRightString(W - 48, 148.5, f"tempo sugerido: {tempo}")
    Frame(44, 20, W - 88, 122, showBoundary=0, leftPadding=4, rightPadding=4, topPadding=2, bottomPadding=0) \
        .addFromList([Paragraph(text, note)], c)


def text_block(c, flowables, x, y_top, w, h):
    Frame(x, y_top - h, w, h, showBoundary=0, leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0) \
        .addFromList(list(flowables), c)


def bullets(items, style=bullet):
    return [Paragraph(t, style, bulletText="•") for t in items]


def image(c, name, x, y, w, h, border=True):
    img = ImageReader(os.path.join(IMG, name))
    iw, ih = img.getSize()
    s = min(w / iw, h / ih)
    dw, dh = iw * s, ih * s
    ox, oy = x + (w - dw) / 2, y + (h - dh) / 2
    c.drawImage(img, ox, oy, dw, dh)
    if border:
        c.setStrokeColor(LINE)
        c.setLineWidth(1)
        c.rect(ox, oy, dw, dh, stroke=1, fill=0)
    return ox, oy, dw, dh


def box(c, x, y, w, h, title, lines=(), fill=white, stroke=LINE, title_color=INK, accent=False):
    c.setFillColor(OLIVE_L if accent else fill)
    c.setStrokeColor(OLIVE if accent else stroke)
    c.setLineWidth(1.6 if accent else 1)
    c.roundRect(x, y, w, h, 8, stroke=1, fill=1)
    c.setFillColor(title_color)
    c.setFont("Segoe-B", 12)
    c.drawString(x + 12, y + h - 20, title)
    c.setFont("Segoe", 10.5)
    c.setFillColor(MUTED)
    for i, ln in enumerate(lines):
        c.drawString(x + 12, y + h - 37 - i * 14, ln)


def arrow(c, x1, y1, x2, y2, color=MUTED, both=False, label=None, lx=0, ly=6):
    import math
    c.setStrokeColor(color)
    c.setFillColor(color)
    c.setLineWidth(1.4)
    c.line(x1, y1, x2, y2)

    def head(xa, ya, xb, yb):
        a = math.atan2(yb - ya, xb - xa)
        p = c.beginPath()
        p.moveTo(xb, yb)
        p.lineTo(xb - 8 * math.cos(a - 0.4), yb - 8 * math.sin(a - 0.4))
        p.lineTo(xb - 8 * math.cos(a + 0.4), yb - 8 * math.sin(a + 0.4))
        p.close()
        c.drawPath(p, stroke=0, fill=1)

    head(x1, y1, x2, y2)
    if both:
        head(x2, y2, x1, y1)
    if label:
        c.setFont("Segoe", 9.5)
        c.setFillColor(MUTED)
        c.drawCentredString((x1 + x2) / 2 + lx, (y1 + y2) / 2 + ly, label)


def table(c, data, x, y_top, widths, head_fill=OLIVE, font_size=10.5):
    rows = [[Paragraph(str(v), cellb if r == 0 else cell) for v in row] for r, row in enumerate(data)]
    t = Table(rows, colWidths=widths)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), head_fill),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [white, HexColor("#EEF0EB")]),
        ("LINEBELOW", (0, 0), (-1, -1), 0.5, LINE),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
        ("LEFTPADDING", (0, 0), (-1, -1), 7),
    ]))
    _, th = t.wrap(sum(widths), 1000)
    t.drawOn(c, x, y_top - th)
    return th


def stat(c, x, y, value, label, color=OLIVE):
    c.setFont("Segoe-B", 26)
    c.setFillColor(color)
    c.drawString(x, y, value)
    c.setFont("Segoe", 10.5)
    c.setFillColor(MUTED)
    c.drawString(x, y - 16, label)


c = canvas.Canvas(OUT, pagesize=(W, H))
c.setTitle("ExpoTech — Apresentação do drone de reconhecimento")
c.setAuthor("ExpoTech")

# 1 ---------------------------------------------------------------- capa
c.setFillColor(HexColor("#1B2024"))
c.rect(0, 0, W, H, stroke=0, fill=1)
image(c, "blender_render.png", 380, 150, 560, 340, border=False)
c.setFillColor(LED)
c.rect(48, 430, 36, 5, stroke=0, fill=1)
c.setFont("Segoe-B", 46)
c.setFillColor(white)
c.drawString(48, 380, "ExpoTech")
c.setFont("Segoe-L", 20)
c.setFillColor(HexColor("#D5DBC4"))
c.drawString(48, 350, "Drone de reconhecimento tático")
c.setFont("Segoe", 13)
c.setFillColor(HexColor("#AEB6BE"))
for i, ln in enumerate(["Arte no Blender · física real na Unity 6", "Integração ROS 2 Humble · sensores e modo autônomo",
                        "Tema inspirado no universo de Rainbow Six"]):
    c.drawString(48, 315 - i * 20, ln)
c.setFont("Segoe-SB", 11)
c.setFillColor(HexColor("#8E979F"))
c.drawString(48, 215, "Roteiro de apresentação · duração total ~15 min")
notes(c, "<b>Abertura.</b> Cumprimente e apresente a equipe. Diga em uma frase o que é o projeto: "
         "<i>\"O ExpoTech é um drone de reconhecimento, inspirado no universo de Rainbow Six, que levamos do desenho no Blender "
         "até um robô simulado com física real, controlado por ROS 2, com sensores e modo autônomo.\"</i> "
         "Avise que no fim haverá uma demonstração ao vivo e espaço para perguntas.", "30 s")
c.showPage()

# 2 ---------------------------------------------------------------- desafio
page(c, 2, "O desafio", "Levar o drone do desenho até um robô que pensa e se move como no mundo real")
cards = [
    ("Arte", ["Modelo 3D feito no Blender:", "corpo, rodas de roletes e LEDs"], OLIVE),
    ("Física", ["Rodas com motor, peso, atrito", "e pulo de verdade na Unity"], BLUE),
    ("ROS 2", ["Mesmo padrão de robôs reais:", "tópicos, sensores e comandos"], AMBER),
    ("Inteligência", ["Sensores que pulam e desviam", "sozinhos e modo autônomo"], LED),
]
cw = (W - 80 - 3 * 18) / 4
for i, (t, lines, col) in enumerate(cards):
    x = 40 + i * (cw + 18)
    c.setFillColor(white)
    c.setStrokeColor(LINE)
    c.roundRect(x, 245, cw, 170, 10, stroke=1, fill=1)
    c.setFillColor(col)
    c.roundRect(x, 405, cw, 10, 4, stroke=0, fill=1)
    c.setFont("Segoe-B", 20)
    c.setFillColor(INK)
    c.drawString(x + 16, 365, t)
    c.setFont("Segoe", 12)
    c.setFillColor(MUTED)
    for j, ln in enumerate(lines):
        c.drawString(x + 16, 335 - j * 18, ln)
c.setFont("Segoe", 12)
c.setFillColor(INK)
c.drawString(40, 210, "Meta: deixar o robô pronto para a expo e pronto para receber o cenário final, também modelado no Blender.")
notes(c, "Explique os quatro pilares, da esquerda para a direita. <b>Arte:</b> o visual é o nosso desenho no Blender. "
         "<b>Física:</b> nada de animação fingida, as rodas têm motor e o drone tem peso. <b>ROS 2:</b> é o padrão usado em "
         "robótica de verdade, então o mesmo software que controla o simulado poderia controlar um robô físico. "
         "<b>Inteligência:</b> sensores que reagem sozinhos e um modo em que o drone anda por conta própria.", "1 min")
c.showPage()

# 3 ---------------------------------------------------------------- arquitetura
page(c, 3, "Arquitetura", "Unity simula o robô; o ROS 2 roda num container Docker; os dois conversam pela porta 10000")
box(c, 40, 330, 170, 90, "Blender", ["arte do drone", "colisões do cenário", "→ exporta FBX"])
box(c, 40, 205, 170, 95, "Claude Code + MCP", ["ferramenta própria", "que configura o", "Editor da Unity"])
c.setFillColor(white); c.setStrokeColor(LINE); c.roundRect(250, 195, 300, 235, 10, stroke=1, fill=1)
c.setFont("Segoe-B", 13); c.setFillColor(INK); c.drawString(264, 408, "Unity 6 (URP) · simulação")
box(c, 264, 330, 272, 66, "expo_drone", ["juntas com motor (ArticulationBody)", "física PhysX, 52,5 kg"], accent=True)
box(c, 264, 262, 272, 58, "Scripts do ExpoTech", ["tração, pulo, sensores, piloto automático"])
box(c, 264, 205, 272, 47, "ROSConnection", ["protocolo ROS 2 · TCP 10000"])
c.setFillColor(white); c.setStrokeColor(LINE); c.roundRect(600, 195, 320, 235, 10, stroke=1, fill=1)
c.setFont("Segoe-B", 13); c.setFillColor(INK); c.drawString(614, 408, "Docker · ROS 2 Humble")
box(c, 614, 330, 292, 66, "ROS-TCP-Endpoint", ["ponte com a Unity na porta 10000"])
box(c, 614, 262, 292, 58, "robot_state_publisher", ["posição das rodas e sensores (URDF)"])
box(c, 614, 205, 292, 47, "Ferramentas ROS 2", ["ros2 topic · RViz · Nav2 (futuro)"])
arrow(c, 210, 375, 262, 363)
arrow(c, 210, 252, 248, 252)
arrow(c, 536, 228, 612, 363, both=True, label="14 tópicos", lx=-6, ly=10)
arrow(c, 760, 330, 760, 322)
notes(c, "Mostre o caminho da esquerda para a direita. A arte sai do <b>Blender</b>. A <b>Unity</b> faz a física e roda os nossos "
         "scripts. O <b>ROS 2</b> roda isolado num container Docker, como rodaria num computador de robô, e conversa com a Unity pela "
         "porta 10000. Destaque que a descrição do robô (URDF) é uma só: a Unity e o ROS leem o mesmo arquivo, então nunca ficam "
         "diferentes. Cite rapidamente que criamos uma ferramenta MCP para o Claude configurar a Unity, o que acelerou o trabalho.",
      "1 min")
c.showPage()

# 4 ---------------------------------------------------------------- blender -> unity
page(c, 4, "Do Blender para a Unity", "A arte final montada sobre um robô físico")
image(c, "arena_front.png", 40, 190, 430, 242)
text_block(c, bullets([
    "<b>Medidas reais do modelo:</b> 2,5 m de largura, rodas de 0,55 m de raio, 52,5 kg no total.",
    "<b>Duas camadas:</b> colisão simples (cilindros) para a física e a malha do Blender por cima, alinhada automaticamente.",
    "<b>Frames padrão ROS:</b> base_link, rodas, câmera, LiDAR e IMU com os mesmos nomes de um robô real.",
    "<b>Remontagem em um comando:</b> trocou a arte ou o URDF, o script rebuild_expo_drone.py reconstrói o drone inteiro.",
]), 495, 430, 425, 250)
notes(c, "Mostre a imagem: é o nosso modelo do Blender dentro da Unity, no ponto de partida da arena. Explique que o que você vê não é "
         "o que colide: por baixo há formas simples (cilindros), que deixam a física rápida e estável, e a arte vai por cima. "
         "Conte um detalhe curioso: no Blender, a roda chamada \"Wheel_L\" fica do lado direito do drone, então mapeamos cada roda "
         "pelo lado real, não pelo nome. Termine dizendo que qualquer mudança na arte é refeita com um único comando.", "1 min")
c.showPage()

# 5 ---------------------------------------------------------------- física
page(c, 5, "Física real: como um drone de duas rodas fica em pé", "O corpo fica pendurado no eixo, como um pêndulo (o mesmo princípio dos throwbots)")
# desenho do pêndulo
cx, cy = 130, 330
c.setStrokeColor(INK); c.setLineWidth(2); c.setFillColor(HexColor("#2B2F33"))
c.circle(cx, cy, 70, stroke=1, fill=1)
c.setFillColor(HexColor("#6F7E4B")); c.circle(cx, cy, 42, stroke=0, fill=1)
c.setFillColor(white); c.circle(cx, cy, 5, stroke=0, fill=1)
c.setFillColor(LED); c.circle(cx, cy - 30, 7, stroke=0, fill=1)
c.setStrokeColor(LED); c.setDash(3, 3); c.line(cx, cy, cx, cy - 30); c.setDash()
c.setStrokeColor(MUTED); c.setLineWidth(0.8)
c.line(cx + 6, cy, 214, cy + 22); c.line(cx + 8, cy - 30, 214, cy - 30)
c.setFont("Segoe-SB", 10.5); c.setFillColor(INK)
c.drawString(218, cy + 19, "eixo das rodas")
c.drawString(218, cy - 27, "centro de massa")
c.setFont("Segoe", 10.5); c.setFillColor(MUTED)
c.drawString(218, cy - 41, "0,3 m abaixo do eixo")
c.setStrokeColor(MUTED); c.setLineWidth(1); c.line(40, cy - 71, 330, cy - 71)
c.setFont("Segoe", 10); c.setFillColor(MUTED); c.drawString(40, cy - 87, "O peso embaixo puxa o corpo de volta para baixo do eixo.")
th = table(c, [["Medida", "Antes", "Depois"],
               ["Inclinação do corpo na arrancada", "−89°", "−45°"],
               ["Inclinação em repouso", "12,6°", "0,0°"],
               ["Giro no lugar em 3 s", "−35°", "−103°"],
               ["Distância para parar a 2,4 m/s", "~2,9 m", "~1,4 m"]],
           360, 430, [280, 120, 120])
text_block(c, bullets([
    "Rampa de aceleração (1 m/s²) e frenagem (2 m/s²): o torque não gira mais o corpo.",
    "Contrapeso 3 cm para trás compensa a câmera na frente.",
    "Colisão da roda estreita (0,16 m): os roletes reais quase não resistem de lado.",
], small), 360, 430 - th - 12, 560, 120)
notes(c, "Esse é o slide mais \"engenharia\". Com só duas rodas, o drone giraria em volta do eixo. A solução é a mesma de robôs de "
         "reconhecimento reais: o centro de massa fica abaixo do eixo, então o corpo sempre volta para baixo, como um pêndulo. "
         "Mostre a tabela: no começo o corpo chegava a −89° só de arrancar. Com rampa de aceleração, contrapeso e colisão estreita "
         "nas rodas, ficou estável e passou a girar três vezes mais. Se perguntarem: todos esses números foram medidos na simulação.",
      "1 min 30 s")
c.showPage()

# 6 ---------------------------------------------------------------- pulo
page(c, 6, "Pulo físico", "Um impulso de verdade, decidido pela física do salto")
stat(c, 40, 395, "2,5 m", "altura alvo do pulo")
stat(c, 175, 395, "2,30 m", "medido no teste")
stat(c, 320, 395, "1,14 s", "tempo no ar sobre a caixa", color=BLUE)
text_block(c, bullets([
    "Todas as peças recebem a mesma variação de velocidade para cima: v = √(2·g·h).",
    "No ar as rodas não empurram: a velocidade da decolagem é a do salto inteiro.",
    "Por isso o drone só pula se a <b>velocidade real</b> já basta para cruzar o obstáculo inteiro.",
    "E só se houver <b>espaço para pousar e girar</b> do outro lado; senão, desvia.",
], small), 40, 340, 440, 160)
image(c, "trap.png", 500, 210, 420, 236)
c.setFont("Segoe", 10); c.setFillColor(MUTED)
c.drawString(500, 196, "Antes da correção: pulava a caixa e ficava preso no vão até o armário.")
notes(c, "Explique que o pulo não é uma animação: é um impulso físico calculado pela fórmula da queda livre. Conte a história do "
         "ajuste: com 1,5 m de pulo, como no script original, o drone não tinha tempo no ar para cruzar a caixa e pousava na quina. "
         "Subimos para 2,5 m e ensinamos o drone a fazer a conta antes de pular. Mostre a imagem: era o caso em que ele pulava e "
         "caía num vão estreito demais para os seus 2,8 m de largura. Agora ele confere o espaço de pouso antes.", "1 min")
c.showPage()

# 7 ---------------------------------------------------------------- sensores
page(c, 7, "Sensores no ROS 2", "Cinco sensores, todos validados contra a geometria da arena")
table(c, [["Sensor", "Tópico ROS 2", "Taxa", "Validação"],
          ["LiDAR 360°", "/scan", "~8 Hz", "parede a 1,12 m (planta: ~1,13 m)"],
          ["IMU", "/imu", "~25 Hz", "9,797 m/s² parado (gravidade)"],
          ["Ultrassom frontal", "/expo_drone/range_front", "~16 Hz", "3,52 m até a caixa (exato)"],
          ["Câmera", "/expo_drone/camera/image/compressed", "~10 Hz", "imagem JPEG 640×480"],
          ["Contato com o chão", "/expo_drone/grounded", "10 Hz", "detecta quando está no ar"],
          ["Odometria + posição", "/odom, /tf, /joint_states", "~25 Hz", "posição e giro das rodas"]],
      40, 430, [150, 290, 80, 360])
c.setFont("Segoe", 11); c.setFillColor(INK)
c.drawString(40, 200, "Ruído realista no LiDAR e na IMU · sensores ignoram o próprio drone · padrão de mensagens do ROS (REP 117)")
notes(c, "Percorra a tabela rapidamente, destacando a coluna de validação. O ponto importante: não basta publicar números, eles "
         "batem com a realidade da cena. O LiDAR mede a parede de trás a 1,12 m quando a planta diz 1,13 m; o ultrassom mede "
         "exatamente 3,52 m até a caixa; e a IMU parada mede a gravidade, 9,8 m/s². Comente que acrescentamos um pequeno ruído, "
         "como nos sensores reais, para o software de navegação ser testado em condições honestas.", "1 min")
c.showPage()

# 8 ---------------------------------------------------------------- assistente
page(c, 8, "Assistente inteligente: pular, desviar ou recuar", "A decisão acontece a cada passo da física, venha o comando do jogador ou do ROS")
box(c, 40, 360, 170, 50, "Obstáculo à frente?", ["sensor na altura das rodas"])
box(c, 250, 360, 170, 50, "Baixo o bastante?", ["menor que o pulo"])
box(c, 250, 280, 170, 50, "Alto: há lado livre?", ["testa de 15° a 90°"])
box(c, 250, 197, 170, 50, "Sem saída: cabe girar?", ["círculo do tamanho do drone"])
box(c, 460, 360, 200, 50, "Velocidade e pouso ok?", ["rápido o bastante + espaço"])
box(c, 720, 372, 200, 36, "PULA", [], accent=True)
box(c, 720, 293, 200, 36, "DESVIA pelo lado livre", [])
box(c, 720, 232, 200, 36, "GIRA no lugar", [])
box(c, 720, 186, 200, 36, "RECUA reto (ré)", [])
arrow(c, 210, 385, 248, 385)
arrow(c, 420, 385, 458, 385, label="sim")
arrow(c, 660, 392, 718, 390, label="sim")
arrow(c, 660, 372, 718, 318, label="não", lx=-14, ly=-14)
arrow(c, 335, 360, 335, 332, label="não", lx=16, ly=-3)
arrow(c, 420, 305, 718, 308, label="sim", lx=-40)
arrow(c, 335, 280, 335, 249, label="não", lx=16, ly=-3)
arrow(c, 420, 230, 718, 250, label="sim", lx=-40)
arrow(c, 420, 210, 718, 204, label="não", lx=-40, ly=-14)
leg = [("LIVRE", HexColor("#3CB371")), ("PULANDO", AMBER), ("DESVIANDO", HexColor("#F08C2B")), ("BLOQUEADO", LED)]
c.setFont("Segoe-SB", 10.5); c.setFillColor(INK); c.drawString(40, 320, "Cor dos LEDs do drone:")
for i, (t, col) in enumerate(leg):
    c.setFillColor(col); c.circle(48, 299 - i * 22, 6, stroke=0, fill=1)
    c.setFillColor(INK); c.setFont("Segoe", 10.5); c.drawString(60, 295 - i * 22, t)
notes(c, "Este é o \"cérebro\" do drone, adaptado do nosso sensor de proximidade original. Leia o fluxo: se o obstáculo é baixo e o "
         "drone tem velocidade e espaço para pousar, ele <b>pula</b>; se não, <b>desvia</b>. Se é alto, procura o lado livre. Se "
         "não há saída, só gira se couber; senão dá <b>ré</b>. Mostre a legenda: na demonstração os LEDs vão mudar de cor conforme a "
         "decisão, o que ajuda o público a entender o que o drone está \"pensando\".", "1 min 30 s")
c.showPage()

# 9 ---------------------------------------------------------------- controles e autonomia
page(c, 9, "Jogável à mão ou andando sozinho", "Painel na tela, teclado, controle de videogame ou ROS 2")
table(c, [["Ação", "Teclado / controle", "ROS 2"],
          ["Dirigir", "WASD / setas · analógico", "/cmd_vel"],
          ["Andar sozinho", "M · Start", "/expo_drone/autopilot_enable"],
          ["Pular", "Espaço · botão A", "/expo_drone/jump"],
          ["Sensores automáticos", "T", "/expo_drone/avoidance_enable"],
          ["Câmera seguir / topo", "C", "—"],
          ["Reposicionar no início", "R", "/expo_drone/reset"]],
      40, 430, [140, 160, 210])
image(c, "auto_a.png", 580, 250, 340, 182)
text_block(c, bullets([
    "Piloto automático segue o caminho mais livre visto pelo LiDAR.",
    "Sem avançar 1,5 m em 8 s, escolhe nova direção em 360°.",
    "60 s sozinho na arena: <b>39 m percorridos</b>, sem ficar preso.",
], small), 580, 240, 340, 70)
c.setFont("Segoe", 10.5); c.setFillColor(MUTED)
c.drawString(40, 205, "Tecla ou analógico pressionado assume o controle a qualquer momento, até no modo autônomo.")
notes(c, "Mostre que o mesmo drone pode ser pilotado pelo público, andar sozinho ou ser comandado pelo ROS. Explique o piloto "
         "automático: ele olha o LiDAR, escolhe o caminho mais aberto e, se perceber que está indo e voltando sem sair do lugar, "
         "muda de direção. Dê o número do teste: 39 metros em um minuto, sem travar. Reforce que, mesmo andando sozinho, basta "
         "tocar numa tecla para assumir o controle.", "1 min")
c.showPage()

# 10 --------------------------------------------------------------- ROS na prática
page(c, 10, "ROS 2 na prática", "Comandos usados na demonstração (dentro do container expotech_ros2)")
cmds = [
    ("Subir o ROS 2", "cd C:\\dev\\ExpoTech\\ros2 ; docker compose up -d"),
    ("Ver os tópicos", "ros2 topic list            (14 tópicos do drone)"),
    ("Medir a taxa do LiDAR", "ros2 topic hz /scan"),
    ("Andar pelo ROS", "ros2 topic pub -r 10 /cmd_vel geometry_msgs/msg/Twist \"{linear: {x: 1.0}}\""),
    ("Pular pelo ROS", "ros2 topic pub -w 1 --once /expo_drone/jump std_msgs/msg/Empty '{}'"),
    ("Ligar o modo autônomo", "ros2 topic pub -w 1 --once /expo_drone/autopilot_enable std_msgs/msg/Bool '{data: true}'"),
]
y = 405
for label, cmd in cmds:
    c.setFont("Segoe-SB", 11.5); c.setFillColor(INK); c.drawString(40, y, label)
    c.setFillColor(HexColor("#2A3036")); c.roundRect(230, y - 7, 690, 24, 5, stroke=0, fill=1)
    c.setFont("Courier-Bold", 10.5); c.setFillColor(HexColor("#D6E3B8")); c.drawString(242, y + 1, cmd)
    y -= 37
notes(c, "Se o público for técnico, abra um terminal e rode dois ou três destes comandos ao vivo. Comece por <b>ros2 topic list</b> "
         "para mostrar que o drone aparece como um robô ROS comum. Depois mande ele <b>pular pelo ROS</b>: é o momento mais visual. "
         "Explique o <b>-w 1</b>: ele espera a Unity estar ouvindo antes de mandar a mensagem, senão o comando pode se perder.",
      "1 min")
c.showPage()

# 11 --------------------------------------------------------------- roteiro demo
page(c, 11, "Roteiro da demonstração ao vivo", "Antes de começar: Docker ligado, container no ar, Unity em Play com a janela em foco")
steps = [
    ("1", "Mostrar a cena", "Câmera seguindo o drone na arena; aponte o painel no canto direito.", "30 s"),
    ("2", "Pilotar à mão", "WASD para andar e girar; Espaço para pular no espaço aberto.", "45 s"),
    ("3", "Sensores", "Ande até a caixa baixa: o drone decide pular; aponte os LEDs mudando de cor.", "45 s"),
    ("4", "Desligar sensores (T)", "Mostre que sem o assistente ele bate; ligue de novo.", "20 s"),
    ("5", "Modo autônomo (M)", "Deixe andar sozinho ~40 s: pula obstáculos baixos e contorna os altos.", "45 s"),
    ("6", "Pelo ROS 2", "No terminal: ros2 topic hz /scan e um pulo pelo ROS.", "30 s"),
    ("7", "Reposicionar (R)", "Volta ao início para o próximo visitante.", "5 s"),
]
y = 408
for n, t, d, tm in steps:
    c.setFillColor(OLIVE); c.circle(52, y + 4, 11, stroke=0, fill=1)
    c.setFont("Segoe-B", 11); c.setFillColor(white); c.drawCentredString(52, y, n)
    c.setFont("Segoe-B", 12); c.setFillColor(INK); c.drawString(74, y, t)
    c.setFont("Segoe", 11.5); c.setFillColor(MUTED); c.drawString(250, y, d)
    c.setFont("Segoe-SB", 11); c.setFillColor(INK); c.drawRightString(920, y, tm)
    y -= 34
notes(c, "<b>Plano B:</b> se o drone travar num canto, aperte <b>R</b>. Se a câmera ficar ruim, aperte <b>C</b> para a vista de cima. "
         "Se o HUD do ROS ficar vermelho, rode <b>docker compose restart</b> na pasta ros2. Se a simulação congelar, clique na janela da "
         "Unity: sem foco o Play Mode pausa. Ensaie esta sequência duas vezes antes da apresentação.", "3 min")
c.showPage()

# 12 --------------------------------------------------------------- desafios
page(c, 12, "Desafios que resolvemos", "Problemas reais que só apareceram nos testes")
table(c, [["Problema", "Causa", "Solução"],
          ["Rodas travadas", "O importador de URDF adiciona um controle de demonstração", "Removido automaticamente pela nossa ferramenta"],
          ["Editor da Unity travado", "Diálogo de atualização de API por um comando antigo", "Código atualizado para a API do Unity 6"],
          ["Corpo girando na arrancada", "Torque das rodas maior que o contrapeso", "Rampa de aceleração + centro de massa mais baixo"],
          ["Drone pousando na caixa", "Pulo de 1,5 m curto demais para a velocidade real", "Pulo de 2,5 m + decisão pela física"],
          ["Preso em vão estreito", "Vão de 2,7 m para um drone de 2,8 m", "Checa espaço de pouso e de giro antes"],
          ["Câmera atrás da parede", "Drone começa encostado na parede", "Câmera que sobe até enxergar o drone"]],
      40, 430, [180, 340, 360])
notes(c, "Escolha dois ou três desses para contar como história, de preferência o do <b>vão estreito</b> e o do <b>pulo curto</b>: "
         "mostram que testamos de verdade e corrigimos com engenharia, não com truques. A mensagem é: simulação com física real "
         "revela problemas que uma animação nunca mostraria, e é exatamente isso que a torna útil antes de construir um robô físico.",
      "1 min")
c.showPage()

# 13 --------------------------------------------------------------- próximos passos
page(c, 13, "Próximos passos", "O que vem depois da expo")
nxt = [
    ("Cenário final no Blender", "Objetos com colisão exportados para a Unity, com passagens de pelo menos 3,5 m."),
    ("Mapa e navegação", "RViz para visualizar e Nav2/SLAM usando /scan e /odom para o drone mapear a arena."),
    ("Executável da demo", "Build Windows com Run In Background: a simulação não pausa sem foco."),
    ("Giro mais preciso", "Hoje gira ~60% do comandado; ajustar atrito e potência das rodas."),
    ("Odometria realista", "Calcular pela rotação das rodas, com erro acumulado, como num robô real."),
]
y = 405
for t, d in nxt:
    c.setFillColor(LED); c.rect(40, y - 2, 4, 18, stroke=0, fill=1)
    c.setFont("Segoe-B", 13); c.setFillColor(INK); c.drawString(54, y, t)
    c.setFont("Segoe", 12); c.setFillColor(MUTED); c.drawString(290, y, d)
    y -= 44
notes(c, "Mostre que o projeto tem continuidade. O passo imediato é o <b>cenário final</b>, que já estamos modelando no Blender. "
         "Depois, ligar o <b>Nav2</b> para o drone construir um mapa sozinho a partir do LiDAR, o que é o uso real desse tipo de "
         "robô. Seja honesto sobre o que ainda falta, como o giro e a odometria realista: isso transmite maturidade técnica.", "45 s")
c.showPage()

# 14 --------------------------------------------------------------- encerramento
c.setFillColor(HexColor("#1B2024"))
c.rect(0, 0, W, H, stroke=0, fill=1)
c.setFillColor(LED); c.rect(48, 470, 36, 5, stroke=0, fill=1)
c.setFont("Segoe-B", 38); c.setFillColor(white); c.drawString(48, 425, "Obrigado!  Perguntas?")
c.setFont("Segoe-SB", 13); c.setFillColor(HexColor("#D5DBC4")); c.drawString(48, 385, "Perguntas prováveis e respostas curtas")
qa = [
    ("Isso funcionaria num robô real?", "Sim: ele fala ROS 2 com mensagens padrão; trocaríamos a Unity pelos drivers do robô."),
    ("Por que Unity e não Gazebo?", "Visual de qualidade para a expo, física PhysX e integração oficial com ROS (Unity Robotics Hub)."),
    ("O pulo é realista?", "A física é real (impulso e gravidade); a altura de 2,5 m é uma escolha de jogo, como no tema."),
    ("Como os sensores sabem o que é obstáculo?", "Raios na altura das rodas e um LiDAR 360°; a decisão usa a física do salto."),
]
y = 345
for q, a in qa:
    c.setFont("Segoe-B", 12); c.setFillColor(white); c.drawString(48, y, q)
    c.setFont("Segoe", 11.5); c.setFillColor(HexColor("#AEB6BE")); c.drawString(48, y - 17, a)
    y -= 44
notes(c, "Agradeça e abra para perguntas. Use as respostas prontas acima se essas perguntas aparecerem. Se não souber algo, "
         "diga que vai verificar: os números de todos os testes estão no relatório do projeto. Termine convidando o público a "
         "pilotar o drone na demonstração.", "1 min ou mais")
c.showPage()
c.save()
print("ok", OUT)
