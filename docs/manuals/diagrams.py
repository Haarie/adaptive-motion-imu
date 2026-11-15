"""Vector diagrams for the manuals (reportlab Flowables)."""
from reportlab.platypus import Flowable
from reportlab.lib import colors
from reportlab.lib.units import mm
from mstyle import INK, GREEN, ORANGE, WHITE, GREY, PANEL, SOFT, GREEN_D, LINE

LEFT = ["0.06", "0.08", "GND", "GND", "0.17", "0.20", "0.22", "0.24", "1.00", "0.11", "1.04", "1.06"]
RIGHT = ["B+", "GND", "RST", "VCC", "0.31", "0.29", "0.02", "1.15", "1.13", "1.11", "0.10", "0.09"]
GY = ["VCC", "GND", "SCL", "SDA", "XDA", "XCL", "AD0", "INT"]
RED = colors.HexColor("#E0352B"); BLK = colors.HexColor("#222222"); YEL = colors.HexColor("#E8B400"); GRN = colors.HexColor("#2FA84F")


class WiringDiagram(Flowable):
    """ProMicro nRF52840 (chip side up, USB-C at the top) wired straight to a GY-521."""
    def __init__(self, width=174 * mm, height=96 * mm):
        Flowable.__init__(self); self.width, self.height = width, height

    def wrap(self, aw, ah): return self.width, self.height

    def draw(self):
        c = self.canv
        p = 2.54 * mm * 1.55                       # drawn pad pitch
        bx, by, bw = 112 * mm, 12 * mm, 40 * mm    # ProMicro board
        bh = 12 * p + 8 * mm
        # --- ProMicro
        c.setFillColor(colors.HexColor("#2E3330")); c.setStrokeColor(INK); c.setLineWidth(0.8)
        c.roundRect(bx, by, bw, bh, 2 * mm, stroke=1, fill=1)
        c.setFillColor(colors.HexColor("#C9CDD1")); c.roundRect(bx + bw / 2 - 5 * mm, by + bh - 3 * mm, 10 * mm, 6 * mm, 1.5 * mm, stroke=1, fill=1)
        c.setFillColor(INK); c.setFont("Raj", 7); c.drawCentredString(bx + bw / 2, by + bh + 4.2 * mm, "USB-C")
        c.saveState(); c.translate(bx + bw / 2, by + bh / 2 + 4 * mm); c.rotate(45)
        c.setFillColor(colors.HexColor("#111111")); c.rect(-5 * mm, -5 * mm, 10 * mm, 10 * mm, stroke=0, fill=1); c.restoreState()
        c.setFillColor(WHITE); c.setFont("Raj", 8.5)
        c.drawCentredString(bx + bw / 2, by + 9 * mm, "ProMicro"); c.drawCentredString(bx + bw / 2, by + 5.5 * mm, "nRF52840")
        pad = {}
        for i in range(12):
            y = by + bh - 6 * mm - i * p
            for side, names, x in (("L", LEFT, bx + 3 * mm), ("R", RIGHT, bx + bw - 3 * mm)):
                used = (side, i) in [("L", 2), ("L", 8), ("L", 9), ("R", 3)]
                c.setFillColor(colors.HexColor("#D9B44A") if not used else GREEN)
                c.setStrokeColor(INK); c.setLineWidth(0.4); c.circle(x, y, 1.25 * mm, stroke=1, fill=1)
                c.setFillColor(WHITE if not used else GREEN); c.setFont("Body" if not used else "BodyB", 6)
                if side == "L": c.drawString(x + 2.2 * mm, y - 2, names[i])
                else: c.drawRightString(x - 2.2 * mm, y - 2, names[i])
                pad[(side, i)] = (x, y)
        # pad numbers
        c.setFillColor(GREY); c.setFont("Body", 5.5)
        for i in range(12):
            y = by + bh - 6 * mm - i * p
            if i not in (2, 8, 9): c.drawRightString(bx - 1.2 * mm, y - 2, str(i + 1))
            if i != 3: c.drawString(bx + bw + 1.2 * mm, y - 2, str(i + 1))
        # --- GY-521
        gx, gw, gh = 14 * mm, 34 * mm, 8 * p + 6 * mm
        gy = by + bh - 6 * mm - 9.5 * p - gh / 2 + 2 * p
        c.setFillColor(colors.HexColor("#1F5FB4")); c.setStrokeColor(INK); c.setLineWidth(0.8)
        c.roundRect(gx, gy, gw, gh, 2 * mm, stroke=1, fill=1)
        c.setFillColor(colors.HexColor("#111111")); c.rect(gx + 8 * mm, gy + gh / 2 - 2.5 * mm, 5 * mm, 5 * mm, stroke=0, fill=1)
        c.setFillColor(WHITE); c.setFont("Raj", 9); c.drawCentredString(gx + 12 * mm, gy + 6 * mm, "GY-521")
        c.setFont("Body", 6); c.drawCentredString(gx + 12 * mm, gy + 3 * mm, "MPU-6050")
        gp = {}
        for i, n in enumerate(GY):
            x, y = gx + gw - 3 * mm, gy + gh - 4 * mm - i * p
            used = i < 4
            c.setFillColor(GREEN if used else colors.HexColor("#D9B44A")); c.setStrokeColor(INK); c.setLineWidth(0.4)
            c.circle(x, y, 1.25 * mm, stroke=1, fill=1)
            c.setFillColor(WHITE); c.setFont("BodyB" if used else "Body", 6); c.drawRightString(x - 2.2 * mm, y - 2, n)
            gp[n] = (x, y)
        c.setFillColor(GREY); c.setFont("Body", 6)
        c.drawString(gx, gy - 4 * mm, "XDA, XCL, AD0, INT: not connected")
        # --- wires (orthogonal)
        def wire(pts, col, label=None, lx=None, ly=None):
            c.setStrokeColor(colors.white); c.setLineWidth(3.2); c.setLineCap(1); c.setLineJoin(1)
            pa = c.beginPath(); pa.moveTo(*pts[0]); [pa.lineTo(*q) for q in pts[1:]]; c.drawPath(pa, stroke=1, fill=0)
            c.setStrokeColor(col); c.setLineWidth(1.9)
            pa = c.beginPath(); pa.moveTo(*pts[0]); [pa.lineTo(*q) for q in pts[1:]]; c.drawPath(pa, stroke=1, fill=0)
        L2, L8, L9, R3 = pad[("L", 2)], pad[("L", 8)], pad[("L", 9)], pad[("R", 3)]
        g_vcc, g_gnd, g_scl, g_sda = gp["VCC"], gp["GND"], gp["SCL"], gp["SDA"]
        # GND: L3 -> GY GND
        xa = g_gnd[0] + 12 * mm
        wire([g_gnd, (xa, g_gnd[1]), (xa, L2[1]), L2], BLK)
        # SDA: 1.00 (L9) -> SDA ; SCL: 0.11 (L10) -> SCL
        xb = g_sda[0] + 20 * mm; xc = g_scl[0] + 26 * mm
        wire([g_sda, (xb, g_sda[1]), (xb, L8[1]), L8], GRN)
        wire([g_scl, (xc, g_scl[1]), (xc, L9[1]), L9], YEL)
        # VCC: R4 -> around the top -> GY VCC
        top = by + bh + 9 * mm; xr = bx + bw + 9 * mm; xv = g_vcc[0] + 6 * mm
        wire([R3, (xr, R3[1]), (xr, top), (xv, top), (xv, g_vcc[1]), g_vcc], RED)
        # legend
        lx, ly = 2 * mm, self.height - 4 * mm
        for i, (col, t) in enumerate([(RED, "VCC 3.3 V  →  GY-521 VCC"), (BLK, "GND  →  GND"),
                                      (GRN, "pad 1.00 (SDA)  →  SDA"), (YEL, "pad 0.11 (SCL)  →  SCL")]):
            y = ly - i * 4.6 * mm
            c.setStrokeColor(col); c.setLineWidth(2.2); c.line(lx, y + 1, lx + 8 * mm, y + 1)
            c.setFillColor(INK); c.setFont("Body", 7.2); c.drawString(lx + 10 * mm, y - 1, t)
        c.setFillColor(GREY); c.setFont("BodyI", 6.5)
        c.drawCentredString(bx + bw / 2, by - 5 * mm, "chip side up, USB-C at the top · pads counted from the USB end")


class SystemDiagram(Flowable):
    """IMU box -> USB -> SimHub PC (plugin) -> UDP -> motion controller -> actuators -> seat (feedback)."""
    def __init__(self, width=174 * mm, height=50 * mm):
        Flowable.__init__(self); self.width, self.height = width, height

    def wrap(self, aw, ah): return self.width, self.height

    def draw(self):
        c = self.canv
        boxes = [("Game", "telemetry", 0), ("SimHub", "Motion add-on", 1), ("Adaptive Motion", "plugin: gains", 2),
                 ("nullCAT", "motion controller", 3), ("Actuators", "+ seat", 4)]
        bw, bh, gap = 29 * mm, 15 * mm, 7.2 * mm
        y = 26 * mm
        pos = {}
        for name, sub, i in boxes:
            x = i * (bw + gap)
            hi = name == "Adaptive Motion"
            c.setFillColor(INK if hi else SOFT); c.setStrokeColor(GREEN if hi else LINE); c.setLineWidth(1.2 if hi else 0.8)
            c.roundRect(x, y, bw, bh, 2 * mm, stroke=1, fill=1)
            c.setFillColor(WHITE if hi else INK); c.setFont("Raj", 10.5); c.drawCentredString(x + bw / 2, y + 8.5 * mm, name)
            c.setFillColor(GREEN if hi else GREY); c.setFont("Body", 6.6); c.drawCentredString(x + bw / 2, y + 4 * mm, sub)
            pos[i] = x
        c.setStrokeColor(INK); c.setFillColor(INK); c.setLineWidth(1)
        labels = ["", "Generic UDP", "rescaled UDP", "EtherCAT"]
        for i in range(4):
            x0, x1 = pos[i] + bw, pos[i + 1]
            c.line(x0 + 1, y + bh / 2, x1 - 2, y + bh / 2)
            pa = c.beginPath(); pa.moveTo(x1 - 1, y + bh / 2); pa.lineTo(x1 - 3.5, y + bh / 2 + 1.6); pa.lineTo(x1 - 3.5, y + bh / 2 - 1.6); pa.close()
            c.drawPath(pa, stroke=0, fill=1)
            if labels[i]: c.setFont("Body", 5.8); c.drawCentredString((x0 + x1) / 2, y + bh / 2 + 2.2 * mm, labels[i]); c.setFont("Body", 6)
        # feedback loop
        xs, xp = pos[4] + bw / 2, pos[2] + bw / 2
        c.setStrokeColor(ORANGE); c.setFillColor(ORANGE); c.setLineWidth(1.4)
        c.line(xs, y - 1, xs, 9 * mm); c.line(xs, 9 * mm, xp, 9 * mm); c.line(xp, 9 * mm, xp, y - 3)
        pa = c.beginPath(); pa.moveTo(xp, y - 1); pa.lineTo(xp - 1.6, y - 3.8); pa.lineTo(xp + 1.6, y - 3.8); pa.close(); c.drawPath(pa, stroke=0, fill=1)
        c.setFillColor(INK); c.setFont("BodyB", 7)
        c.drawCentredString((xs + xp) / 2, 11 * mm, "IMU box on the seat frame  →  USB, 250 frames/s")
        c.setFont("Body", 6.4); c.setFillColor(GREY)
        c.drawCentredString((xs + xp) / 2, 5 * mm, "what the seat really does: tilt, rates, cue, vibration, impacts")


class AxesDiagram(Flowable):
    """Box on the cross-member, seen from the driver's seat side, with the sensor axes."""
    def __init__(self, width=174 * mm, height=52 * mm):
        Flowable.__init__(self); self.width, self.height = width, height

    def wrap(self, aw, ah): return self.width, self.height

    def draw(self):
        c = self.canv
        # beam
        c.setFillColor(colors.HexColor("#1C1D20")); c.rect(4 * mm, 8 * mm, 86 * mm, 18 * mm, stroke=0, fill=1)
        c.setFillColor(colors.HexColor("#3A3D42")); c.rect(4 * mm, 12 * mm, 86 * mm, 2 * mm, stroke=0, fill=1); c.rect(4 * mm, 20 * mm, 86 * mm, 2 * mm, stroke=0, fill=1)
        # box
        c.setFillColor(colors.HexColor("#2B302C")); c.setStrokeColor(INK); c.roundRect(24 * mm, 14 * mm, 44 * mm, 22 * mm, 2 * mm, stroke=1, fill=1)
        c.setFillColor(colors.HexColor("#3A403C")); c.rect(28 * mm, 9 * mm, 36 * mm, 6 * mm, stroke=1, fill=1)
        for x in (36 * mm, 56 * mm):
            c.setFillColor(colors.HexColor("#111111")); c.circle(x, 12 * mm, 1.8 * mm, stroke=0, fill=1)
        c.setFillColor(ORANGE); c.setFont("Raj", 8); c.drawString(44 * mm, 27 * mm, "UP")
        c.setFillColor(WHITE); c.setFont("Raj", 6.5); c.drawString(44 * mm, 21 * mm, "LID = FRONT")
        c.setFillColor(GREY); c.setFont("Body", 6); c.drawString(4 * mm, 3 * mm, "seen from the front of the rig (looking at the lid) · flange + M6 bolts in the beam's lower slot")
        # axes
        ox, oy = 118 * mm, 14 * mm
        def arrow(dx, dy, col, label):
            c.setStrokeColor(col); c.setFillColor(col); c.setLineWidth(1.6)
            c.line(ox, oy, ox + dx, oy + dy)
            import math
            a = math.atan2(dy, dx); s = 2.4 * mm
            pa = c.beginPath(); pa.moveTo(ox + dx, oy + dy)
            pa.lineTo(ox + dx - s * math.cos(a - 0.4), oy + dy - s * math.sin(a - 0.4))
            pa.lineTo(ox + dx - s * math.cos(a + 0.4), oy + dy - s * math.sin(a + 0.4)); pa.close(); c.drawPath(pa, stroke=0, fill=1)
            c.setFont("BodyB", 7.2)
            if dx >= 0: c.drawString(ox + dx + 1.5 * mm, oy + dy + 1 * mm, label)
            else: c.drawString(ox + dx, oy + dy + 2.2 * mm, label)
        arrow(0, 26 * mm, GRN, "+X  up")
        arrow(-24 * mm, 0, colors.HexColor("#1F5FB4"), "+Y  to USB end")
        c.setFillColor(ORANGE); c.setStrokeColor(ORANGE); c.circle(ox, oy, 2.2 * mm, stroke=1, fill=0); c.circle(ox, oy, 0.7 * mm, stroke=0, fill=1)
        c.setFont("BodyB", 7.2); c.drawString(ox + 3.5 * mm, oy - 4.5 * mm, "+Z  out of the lid = forward")
        c.setFillColor(GREY); c.setFont("Body", 6.3)
        c.drawString(ox - 4 * mm, oy + 32 * mm, "sensor axes, measured on the bench")
