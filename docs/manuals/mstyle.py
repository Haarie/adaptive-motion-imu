"""Shared Sim-Spares manual styling for reportlab (fonts, colours, page template, flowables)."""
import math, os
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT, TA_CENTER
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph, Spacer, Table, TableStyle,
                                Image, KeepTogether, Flowable, PageBreak, NextPageTemplate, CondPageBreak)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONTS = os.path.join(ROOT, "enclosure", "fonts")
DEJAVU = "/usr/share/fonts/truetype/dejavu"
pdfmetrics.registerFont(TTFont("Raj", os.path.join(FONTS, "Rajdhani-Bold.ttf")))
pdfmetrics.registerFont(TTFont("RajSB", os.path.join(FONTS, "Rajdhani-SemiBold.ttf")))
pdfmetrics.registerFont(TTFont("Body", os.path.join(DEJAVU, "DejaVuSans.ttf")))
pdfmetrics.registerFont(TTFont("BodyB", os.path.join(DEJAVU, "DejaVuSans-Bold.ttf")))
pdfmetrics.registerFont(TTFont("BodyI", os.path.join(DEJAVU, "DejaVuSans-Oblique.ttf")))
pdfmetrics.registerFont(TTFont("Mono", os.path.join(DEJAVU, "DejaVuSansMono.ttf")))
from reportlab.pdfbase.pdfmetrics import registerFontFamily
registerFontFamily("Body", normal="Body", bold="BodyB", italic="BodyI", boldItalic="BodyB")

INK = colors.HexColor("#0A0C0B"); PANEL = colors.HexColor("#171B19"); LINE = colors.HexColor("#D9DDDA")
GREEN = colors.HexColor("#A6FF4D"); GREEN_D = colors.HexColor("#4C8A12"); ORANGE = colors.HexColor("#FF7A29")
WHITE = colors.HexColor("#F3F5F1"); GREY = colors.HexColor("#5E6661"); SOFT = colors.HexColor("#F2F5F0")
WARN_BG = colors.HexColor("#FFF1E8"); TIP_BG = colors.HexColor("#F1FBE6")

W, H = A4
M = 18 * mm

S = {
    "h1": ParagraphStyle("h1", fontName="Raj", fontSize=24, leading=27, textColor=INK, spaceBefore=4, spaceAfter=8),
    "h2": ParagraphStyle("h2", fontName="Raj", fontSize=16, leading=19, textColor=INK, spaceBefore=12, spaceAfter=5),
    "h3": ParagraphStyle("h3", fontName="RajSB", fontSize=13, leading=16, textColor=GREEN_D, spaceBefore=8, spaceAfter=3),
    "p": ParagraphStyle("p", fontName="Body", fontSize=9.3, leading=13.6, textColor=INK, spaceAfter=5),
    "small": ParagraphStyle("small", fontName="Body", fontSize=7.8, leading=10.5, textColor=GREY, spaceAfter=4),
    "cap": ParagraphStyle("cap", fontName="BodyI", fontSize=7.8, leading=10.5, textColor=GREY, alignment=TA_CENTER, spaceBefore=2, spaceAfter=8),
    "cell": ParagraphStyle("cell", fontName="Body", fontSize=8.2, leading=10.8, textColor=INK),
    "cellb": ParagraphStyle("cellb", fontName="BodyB", fontSize=8.2, leading=10.8, textColor=INK),
    "th": ParagraphStyle("th", fontName="RajSB", fontSize=9.5, leading=11, textColor=WHITE),
    "code": ParagraphStyle("code", fontName="Mono", fontSize=8, leading=11, textColor=INK),
    "step": ParagraphStyle("step", fontName="Body", fontSize=9.3, leading=13.4, textColor=INK, leftIndent=0),
    "boxp": ParagraphStyle("boxp", fontName="Body", fontSize=8.8, leading=12.6, textColor=INK),
}

def P(t, st="p"): return Paragraph(t, S[st])
def H1(t): return P(t, "h1")
def H2(t): return P(t, "h2")
def H3(t): return P(t, "h3")
def C(t): return Paragraph(t, S["cell"])

# ---------------------------------------------------------------- logo (same geometry as the website SVG)
def _arc(cx, cy, r, a0, a1, n=40):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]

def draw_logo(c, x, y, size, dark=True):
    """Steering-wheel logo, 100x100 SVG units scaled to `size`, lower-left corner at (x, y)."""
    k = size / 100.0
    def T(p): return (x + p[0] * k, y + (100 - p[1]) * k)      # SVG y-down -> PDF y-up
    def stroke(pts, w, col):
        c.saveState(); c.setStrokeColor(col); c.setLineWidth(w * k); c.setLineCap(1); c.setLineJoin(1)
        pa = c.beginPath(); q = T(pts[0]); pa.moveTo(*q)
        for p in pts[1:]: pa.lineTo(*T(p))
        c.drawPath(pa, stroke=1, fill=0); c.restoreState()
    rim = _arc(50, 50, 40, -90, 49.46) + _arc(50, 64, 26, 0, -180) + _arc(50, 50, 40, 130.54, 270)
    for e in [(50, 18), (26, 64), (74, 64)]: stroke([(50, 50), e], 5, WHITE if dark else INK)
    stroke(_arc(24.8, 44.0, 10, 191.5, 271.1, 12) + [(31, 34)], 6, ORANGE)
    stroke([(100 - p[0], p[1]) for p in _arc(24.8, 44.0, 10, 191.5, 271.1, 12)] + [(69, 34)], 6, ORANGE)
    stroke(rim, 6, GREEN)
    c.saveState(); c.setFillColor(INK if dark else WHITE); c.setStrokeColor(GREEN); c.setLineWidth(4 * k)
    cx, cy = T((50, 50)); c.circle(cx, cy, 10 * k, stroke=1, fill=1); c.restoreState()

class Logo(Flowable):
    def __init__(self, size, dark=False): Flowable.__init__(self); self.size = size; self.dark = dark
    def wrap(self, aw, ah): return self.size, self.size
    def draw(self): draw_logo(self.canv, 0, 0, self.size, self.dark)

# ---------------------------------------------------------------- boxes, tables, figures
def callout(kind, title, body):
    bg, bar = {"tip": (TIP_BG, GREEN_D), "warn": (WARN_BG, ORANGE), "note": (SOFT, GREY)}[kind]
    inner = [Paragraph("<font name='RajSB' size='10.5'>%s</font>" % title, S["boxp"])]
    for b in (body if isinstance(body, list) else [body]):
        inner.append(Paragraph(b, S["boxp"]))
    t = Table([[inner]], colWidths=[W - 2 * M - 4])
    t.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), bg), ("LINEBEFORE", (0, 0), (0, -1), 3, bar),
                           ("LEFTPADDING", (0, 0), (-1, -1), 9), ("RIGHTPADDING", (0, 0), (-1, -1), 9),
                           ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 7)]))
    return KeepTogether([Spacer(1, 3), t, Spacer(1, 6)])

def table(rows, widths, header=True, zebra=True, mono_cols=()):
    data = []
    for i, r in enumerate(rows):
        row = []
        for j, v in enumerate(r):
            if isinstance(v, Flowable): row.append(v)
            elif header and i == 0: row.append(Paragraph(str(v), S["th"]))
            elif j in mono_cols: row.append(Paragraph(str(v), S["code"]))
            else: row.append(Paragraph(str(v), S["cell"]))
        data.append(row)
    tw = W - 2 * M
    t = Table(data, colWidths=[w * tw for w in widths], repeatRows=1 if header else 0)
    st = [("VALIGN", (0, 0), (-1, -1), "TOP"), ("LEFTPADDING", (0, 0), (-1, -1), 5), ("RIGHTPADDING", (0, 0), (-1, -1), 5),
          ("TOPPADDING", (0, 0), (-1, -1), 3.5), ("BOTTOMPADDING", (0, 0), (-1, -1), 3.5),
          ("LINEBELOW", (0, 0), (-1, -1), 0.4, LINE)]
    if header: st += [("BACKGROUND", (0, 0), (-1, 0), PANEL), ("LINEBELOW", (0, 0), (-1, 0), 1.5, GREEN)]
    if zebra:
        for i in range(1 if header else 0, len(data)):
            if i % 2 == 0: st.append(("BACKGROUND", (0, i), (-1, i), SOFT))
    t.setStyle(TableStyle(st))
    return t

def steps(items, start=1):
    rows = []
    for i, it in enumerate(items, start):
        badge = Table([[Paragraph("<font name='Raj' size='11' color='#0A0C0B'>%d</font>" % i, ParagraphStyle("b", alignment=TA_CENTER, leading=12))]],
                      colWidths=[6.2 * mm], rowHeights=[6.2 * mm])
        badge.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), GREEN), ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                                   ("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                                   ("TOPPADDING", (0, 0), (-1, -1), 0), ("BOTTOMPADDING", (0, 0), (-1, -1), 1)]))
        body = [Paragraph(x, S["step"]) for x in (it if isinstance(it, list) else [it])]
        rows.append([badge, body])
    t = Table(rows, colWidths=[9 * mm, W - 2 * M - 9 * mm])
    t.setStyle(TableStyle([("VALIGN", (0, 0), (-1, -1), "TOP"), ("LEFTPADDING", (0, 0), (-1, -1), 0),
                           ("BOTTOMPADDING", (0, 0), (-1, -1), 5), ("TOPPADDING", (0, 0), (-1, -1), 0)]))
    return t

def code(text):
    t = Table([[Paragraph(text.replace("\n", "<br/>"), S["code"])]], colWidths=[W - 2 * M])
    t.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#EEF1ED")),
                           ("LINEBEFORE", (0, 0), (0, -1), 2, GREEN_D),
                           ("LEFTPADDING", (0, 0), (-1, -1), 8), ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 6)]))
    return KeepTogether([t, Spacer(1, 6)])

def figure(path, width_frac, caption=None, max_h=None):
    from PIL import Image as PImage
    iw, ih = PImage.open(path).size
    w = (W - 2 * M) * width_frac; h = w * ih / iw
    if max_h and h > max_h: h = max_h; w = h * iw / ih
    items = [Image(path, width=w, height=h)]
    if caption: items.append(Paragraph(caption, S["cap"]))
    return KeepTogether(items)

# ---------------------------------------------------------------- document template
class ManualDoc(BaseDocTemplate):
    def __init__(self, path, title, subtitle, version, kicker):
        BaseDocTemplate.__init__(self, path, pagesize=A4, leftMargin=M, rightMargin=M, topMargin=22 * mm, bottomMargin=18 * mm,
                                 title=title, author="Sim-Spares", subject=subtitle, creator="Sim-Spares")
        self.mtitle, self.subtitle, self.version, self.kicker = title, subtitle, version, kicker
        body = Frame(M, 18 * mm, W - 2 * M, H - 40 * mm, id="body")
        cover = Frame(M, 20 * mm, W - 2 * M, 110 * mm, id="cover")
        self.addPageTemplates([PageTemplate("cover", [cover], onPage=self._cover),
                               PageTemplate("body", [body], onPage=self._page)])

    def _cover(self, c, doc):
        c.saveState()
        c.setFillColor(INK); c.rect(0, 0, W, H, stroke=0, fill=1)
        # carbon-ish diagonal hatch
        c.setStrokeColor(colors.HexColor("#121513")); c.setLineWidth(1.2)
        for i in range(-60, 60):
            c.line(i * 12 * mm / 2.5, 0, i * 12 * mm / 2.5 + H, H)
        c.setFillColor(INK); c.rect(0, 0, W, 135 * mm, stroke=0, fill=1)
        # gradient bar
        n = 60
        for i in range(n):
            t = i / (n - 1)
            col = colors.Color(GREEN.red + (ORANGE.red - GREEN.red) * t, GREEN.green + (ORANGE.green - GREEN.green) * t, GREEN.blue + (ORANGE.blue - GREEN.blue) * t)
            c.setFillColor(col); c.rect(i * W / n, 135 * mm, W / n + 1, 2.2 * mm, stroke=0, fill=1)
        draw_logo(c, M, H - 58 * mm, 34 * mm, dark=True)
        c.setFillColor(WHITE); c.setFont("Raj", 15); c.drawString(M + 40 * mm, H - 35 * mm, "SIM-SPARES.COM")
        c.setFillColor(GREEN); c.setFont("RajSB", 11); c.drawString(M + 40 * mm, H - 42 * mm, self.kicker.upper())
        c.setFillColor(WHITE); c.setFont("Raj", 40)
        y = H - 105 * mm
        for line in self.mtitle.split("\n"):
            c.drawString(M, y, line); y -= 15 * mm
        c.setFillColor(colors.HexColor("#B8C0BA")); c.setFont("RajSB", 15); c.drawString(M, y - 2 * mm, self.subtitle)
        c.setFillColor(GREY); c.setFont("Body", 8)
        c.drawString(M, 10 * mm, self.version)
        c.drawRightString(W - M, 10 * mm, "github.com/Haarie/adaptive-motion-imu")
        c.restoreState()

    def _page(self, c, doc):
        c.saveState()
        c.setFillColor(INK); c.rect(0, H - 13 * mm, W, 13 * mm, stroke=0, fill=1)
        c.setFillColor(GREEN); c.rect(0, H - 13.8 * mm, W * 0.6, 0.8 * mm, stroke=0, fill=1)
        c.setFillColor(ORANGE); c.rect(W * 0.6, H - 13.8 * mm, W * 0.4, 0.8 * mm, stroke=0, fill=1)
        draw_logo(c, M, H - 11 * mm, 9 * mm, dark=True)
        c.setFillColor(WHITE); c.setFont("Raj", 11); c.drawString(M + 12 * mm, H - 8 * mm, "SIM-SPARES")
        c.setFillColor(colors.HexColor("#B8C0BA")); c.setFont("RajSB", 10)
        c.drawRightString(W - M, H - 8 * mm, self.mtitle.replace("\n", " ") + "  ·  " + self.subtitle)
        c.setFillColor(GREY); c.setFont("Body", 7.5)
        c.drawString(M, 9 * mm, self.version)
        c.drawRightString(W - M, 9 * mm, "%d" % doc.page)
        c.restoreState()

def start(doc_story):
    return [NextPageTemplate("body"), PageBreak()] + doc_story

def toc_block(entries):
    rows = [[Paragraph("<font name='Raj' size='12' color='#4C8A12'>%02d</font>" % i, S["cell"]), Paragraph("<font name='RajSB' size='11.5'>%s</font>" % t, S["cell"]),
             Paragraph(d, S["small"])] for i, (t, d) in enumerate(entries, 1)]
    t = Table(rows, colWidths=[12 * mm, 62 * mm, W - 2 * M - 74 * mm])
    t.setStyle(TableStyle([("VALIGN", (0, 0), (-1, -1), "MIDDLE"), ("LINEBELOW", (0, 0), (-1, -1), 0.4, LINE),
                           ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 4)]))
    return t
