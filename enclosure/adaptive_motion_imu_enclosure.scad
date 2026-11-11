// AdaptiveMotionIMU enclosure — ProMicro nRF52840 (nice!nano clone, USB-C, headerless) + GY-521 (MPU-6050)
// (board = "micro" still gives the original Arduino Micro + level-shifter layout)
// Rigid two-part box that bolts to aluminium extrusion with two M6 bolts + T-nuts (30/40 series, slot 8).
// Default "side": on the vertical side face of a horizontal cross-member, both bolts in its horizontal slot.
// Design rule: the sensor must move EXACTLY like the seat. So: thick floor, sensor screwed to
// bosses that grow out of the floor, two M6 bolts into one slot of the profile, print in PETG/ASA, 100 % infill
// around the sensor pocket (or simply 6 perimeters + 40 % gyroid everywhere).
//
// Render parts:  openscad -D 'part="base"'  -o base.stl  adaptive_motion_imu_enclosure.scad
//                openscad -D 'part="lid"'   -o lid.stl   ...
//                openscad -D 'part="inlay_green"' -o lid_inlay_green.stl ...  (+ inlay_orange, inlay_white: MMU3 colours)
//                openscad -D 'part="assembly"' -o preview.stl ...
//
// MEASURE YOUR BOARDS WITH CALIPERS and adjust the "board" section — clone boards vary ±0.5 mm.

part = "assembly";      // base | lid | inlay | assembly | fit | exploded | mounted
board = "nrf52840";     // nrf52840 (ProMicro nRF52840, 3.3 V, USB-C) | micro (Arduino Micro, 5 V, micro-USB)
$fn = 48;

// ---------------- boards ------------------------------------------------------------------
is_nrf = board == "nrf52840";
// "micro_*" = the controller board, whichever it is
micro_l = is_nrf ? 33.3 : 48.3;  micro_w = is_nrf ? 18.0 : 17.8;  micro_t = 1.6;
micro_under = 2.6;      // clearance under the PCB (headerless: only pads underneath)
micro_over  = 4.2;      // tallest part on top (USB receptacle)
usb_w = is_nrf ? 9.0 : 8.0;  usb_h = is_nrf ? 3.3 : 3.2;  usb_overhang = is_nrf ? 1.0 : 0.8;  // USB-C / micro-B receptacle
usb_cut_w = is_nrf ? 13.0 : 11.0;   usb_cut_h = is_nrf ? 8.0 : usb_h + 4.5;   // room for the plug overmould
use_shifter = !is_nrf;  // 3.3 V board: no level shifter needed

gy_l = 21.2;  gy_w = 16.0;  gy_t = 1.6;              // GY-521 PCB
gy_hole_d = 3.2;  gy_hole_pitch = 15.5;  gy_hole_inset = 2.6;   // two holes along one long edge
gy_screw_pilot = 2.2;   // M2.5 self-tapping into PETG (or 2.5 for a loose fit + nut)
gy_standoff = 3.0;      // gap under the GY-521 for its header stubs / SMD parts

shifter_l = 14; shifter_w = 11;   // BSS138 4-ch level shifter (sits on a pad, double-sided tape)

// ---------------- box ---------------------------------------------------------------------
wall = 2.4;
floor_t = 4.0;          // thick: stiffness under the sensor
lid_t = 2.4;
gap = 0.4;              // clearance around boards
lug_d = 8.0;            // external lugs for the lid screws (keeps the cavity free)
insert_d = 4.0;  insert_depth = 5.0;   // M3 x 5.7 heat-set insert hole (check your inserts)
lid_screw_d = 3.4;  lid_head_d = 6.2;  lid_head_h = 1.6;

inner_l = usb_overhang + 0.6 + micro_l + gap + 4 + gy_l + 3 + (use_shifter ? shifter_l + gap + 1 : 0);
inner_w = max(micro_w, gy_w, use_shifter ? shifter_w : 0) + 2 * gap + 4.0;
inner_h = micro_under + micro_t + micro_over + 3.0;
outer_l = inner_l + 2 * wall;
outer_w = inner_w + 2 * wall;
base_h  = floor_t + inner_h;

// ---------------- mounting ----------------------------------------------------------------
// "side":   (default) a flange along the box's long side carries both bolts in a row parallel to the box,
//           so the box lies along a horizontal beam, bolted into the slot of its vertical side face; the
//           box sits beside (above or below) the slot. Bolt heads stay clear of the lugs and the USB plug.
// "across": the box lies across the profile (e.g. a vertical upright, box horizontal); a mounting
//           plate under the middle of the box carries the two bolts above and below the box, both in
//           the same profile slot, so the box can't twist. Slots are long along the profile.
// "along":  ears at both ends, bolts along the box's long axis (profile parallel to the box).
// The box need not be square to the rig: the plugin's Auto-align measures the real mounting angle.
mount_style = "side";
side_pitch = 32;        // bolt spacing along the profile ("side")
side_flange = 20;       // flange reach below the box side ("side")
bolt_d = 6.6;           // M6 clearance
bolt_head_d = 11.0;     // DIN 912 / ISO 7380 M6 head + washer room
bolt_slot = 6;          // slot length (mm of play along the profile)
plate_t = 6.0;          // mounting plate thickness ("across")
plate_l = 34;           // plate length along the box (x) ("across")
plate_ext = 20;         // plate reach beyond each long side of the box (y) ("across")
bolt_off = plate_ext / 2 + 1;   // bolt centre distance from the box side ("across")
ear_l = 16;  ear_t = 6.0;   // ears ("along"), and the flat cable-tie tab at the USB end ("across")
tab_l = 10;

// positions (inside-cavity coordinates, origin at inner corner)
micro_x0 = usb_overhang + 0.6;             // PCB starts here; USB receptacle ~0.6 mm from the wall
micro_y0 = (inner_w - micro_w) / 2;
micro_z0 = micro_under;                      // PCB underside height above cavity floor
gy_x0 = micro_x0 + micro_l + 4;
gy_y0 = (inner_w - gy_w) / 2;
sh_x0 = gy_x0 + gy_l + 3;
sh_y0 = (inner_w - shifter_w) / 2;

module rounded_box(l, w, h, r = 2) {
    hull() for (x = [r, l - r], y = [r, w - r]) translate([x, y, 0]) cylinder(r = r, h = h);
}

use <fonts/Rajdhani-Bold.ttf>     // Sim-Spares brand font (SIL OFL, bundled)
BRAND_FONT = "Rajdhani:style=Bold";
logo_d = 22;            // steering-wheel logo size on the lid, mm

// ---- Sim-Spares steering-wheel logo (same geometry as the website's SVG, 100 x 100 units, y down) ----
function svgp(p) = [p[0] - 50, 50 - p[1]];                         // SVG -> centred, y up
function arc(c, r, a0, a1, n = 36) = [ for (i = [0 : n]) let(a = a0 + (a1 - a0) * i / n) svgp([c[0] + r * cos(a), c[1] + r * sin(a)]) ];
module stroke(pts, w) { for (i = [0 : len(pts) - 2]) hull() { translate(pts[i]) circle(d = w, $fn = 16); translate(pts[i + 1]) circle(d = w, $fn = 16); } }
rim_pts = concat(arc([50, 50], 40, -90, 49.46, 48), arc([50, 64], 26, 0, -180, 36), arc([50, 50], 40, 130.54, 270, 48));
module logo_green() { stroke(rim_pts, 6); difference() { circle(r = 12, $fn = 48); circle(r = 8, $fn = 48); } }
module logo_orange() {
    difference() {
        for (m = [0, 1]) mirror([m, 0]) stroke(concat(arc([24.8, 44.0], 10, 191.5, 271.1, 12), [svgp([31, 34])]), 6);
        logo_green();
    }
}
module logo_white() {
    difference() {
        for (e = [[50, 18], [26, 64], [74, 64]]) stroke([svgp([50, 50]), svgp(e)], 5);
        circle(r = 12, $fn = 48);
        logo_green();
    }
}
module logo2d(c) scale(logo_d / 100) {
    if (c == "green") logo_green(); else if (c == "orange") logo_orange(); else if (c == "white") logo_white();
}

// Lid marking. On the "side" mount the box's long axis runs along the (transverse) beam, so the rig's
// forward direction points out of the lid: the lid shows UP (away from the flange) instead.
label = mount_style == "side" ? "up" : "fwd";
arrow2d = [[0, -2], [14, -2], [14, -4.5], [21, 0], [14, 4.5], [14, 2], [0, 2]];

// 2D artwork per inlay colour: green = logo rim + hub, orange = paddles + direction, white = spokes + text
module label2d(c) {
    lx = logo_d / 2 + 2.5;                       // logo centre from the -X (USB) end
    rx = (lx + logo_d / 2 + 1.5 + outer_l - 2.5) / 2;  // centre of the text column
    translate([lx, outer_w / 2]) logo2d(c);
    if (c == "white") {
        translate([rx, outer_w / 2 + 5.4]) resize([outer_l - 2.5 - (lx + logo_d / 2 + 1.5), 0], auto = true)
            text("SIM-SPARES.COM", size = 5, font = BRAND_FONT, halign = "center", valign = "center");
        if (label == "up") translate([rx + 9.5, outer_w / 2 - 5.8]) text("LID = FRONT", size = 3.4, font = BRAND_FONT, halign = "center", valign = "center");
    }
    if (c == "orange") {
        if (label == "up") {
            translate([rx - 7, outer_w / 2 - 5.8]) rotate(90) scale(0.5) translate([-10.5, 0]) polygon(arrow2d);    // points +Y
            translate([rx - 13.5, outer_w / 2 - 5.8]) text("UP", size = 4.6, font = BRAND_FONT, halign = "center", valign = "center");
        } else {
            translate([rx - 9, outer_w / 2 - 5.8]) text("FWD", size = 4.6, font = BRAND_FONT, halign = "center", valign = "center");
            translate([rx + 1, outer_w / 2 - 5.8]) scale([0.75, 0.6]) polygon(arrow2d);                            // points +X
        }
    }
}
inlay_colours = ["green", "orange", "white"];
module text_label(depth, c = "all") {
    linear_extrude(depth) if (c == "all") for (k = inlay_colours) label2d(k); else label2d(c);
}

module base() {
    difference() {
        union() {
            rounded_box(outer_l, outer_w, base_h);
            // lid-screw lugs on the long sides
            for (p = lug_xy()) hull() {
                translate([p[0], p[1], 0]) cylinder(d = lug_d, h = base_h);
                translate([p[0] - lug_d / 2, p[1] < 0 ? 0.5 : outer_w - 1.5, 0]) cube([lug_d, 1, base_h]);
            }
            if (mount_style == "along") {
                // -X ear stays flat so the USB plug clears it; +X ear gets a stiffening gusset
                translate([-ear_l, 0, 0]) cube([ear_l + 1, outer_w, ear_t]);
                translate([outer_l - 1, 0, 0]) hull() {
                    cube([ear_l + 1, outer_w, ear_t]);
                    cube([1, outer_w, ear_t + 5]);
                }
            } else if (mount_style == "side") {
                // flange along the -Y side of the box, under its middle part
                translate([outer_l / 2 - side_pitch / 2 - 13, -side_flange, 0]) cube([side_pitch + 26, side_flange + 1, plate_t]);
                // three gussets tie the flange to the side wall, clear of the bolt heads (head + slot play)
                for (gx = [outer_l / 2 - side_pitch / 2 - 13, outer_l / 2 - 1.5, outer_l / 2 + side_pitch / 2 + 10])
                    hull() {
                        translate([gx, -side_flange + 1, 0]) cube([3, 1, plate_t]);
                        translate([gx, 0, 0]) cube([3, 1, base_h - 3]);
                    }
                translate([-tab_l, 0, 0]) cube([tab_l + 1, outer_w, 4]);     // cable-tie tab at the USB end
            } else {
                // mounting plate under the middle of the box, reaching past both long sides
                translate([outer_l / 2 - plate_l / 2, -plate_ext, 0]) cube([plate_l, outer_w + 2 * plate_ext, plate_t]);
                // four gussets tie the plate to the side walls, clear of the bolt heads
                for (gx = [outer_l / 2 - plate_l / 2, outer_l / 2 + plate_l / 2 - 3], side = [0, 1])
                    hull() {
                        translate([gx, side == 0 ? -plate_ext + 1 : outer_w + plate_ext - 2, 0]) cube([3, 1, plate_t]);
                        translate([gx, side == 0 ? 0 : outer_w - 1, 0]) cube([3, 1, base_h - 3]);
                    }
                // flat tab at the USB end for cable-tie strain relief
                translate([-tab_l, 0, 0]) cube([tab_l + 1, outer_w, 4]);
            }
        }
        // cavity
        translate([wall, wall, floor_t]) rounded_box(inner_l, inner_w, inner_h + 1, 1);
        if (mount_style == "along") {
            // M6 slots in the ears, long along the box
            for (x = [-ear_l / 2 - 2, outer_l + ear_l / 2]) translate([x, outer_w / 2, -1])
                hull() for (dx = [-bolt_slot / 2, bolt_slot / 2]) translate([dx, 0, 0]) cylinder(d = bolt_d, h = ear_t + 10);
        } else if (mount_style == "side") {
            // M6 slots in the flange, long along the profile (x)
            for (x = [outer_l / 2 - side_pitch / 2, outer_l / 2 + side_pitch / 2]) translate([x, -side_flange / 2 - 1, -1])
                hull() for (dx = [-bolt_slot / 2, bolt_slot / 2]) translate([dx, 0, 0]) cylinder(d = bolt_d, h = plate_t + 10);
        } else {
            // M6 slots in the plate, long along the profile (y)
            for (y = [-bolt_off, outer_w + bolt_off]) translate([outer_l / 2, y, -1])
                hull() for (dy = [-bolt_slot / 2, bolt_slot / 2]) translate([0, dy, 0]) cylinder(d = bolt_d, h = plate_t + 10);
        }
        // USB opening in the -X wall, centred on the board's receptacle (room for the plug overmould)
        translate([-1, wall + micro_y0 + micro_w / 2 - usb_cut_w / 2, floor_t + micro_z0 + micro_t + usb_h / 2 - usb_cut_h / 2])
            cube([wall + 2, usb_cut_w, usb_cut_h]);
        // heat-set insert holes in the lugs
        for (p = lug_xy()) translate([p[0], p[1], base_h - insert_depth]) cylinder(d = insert_d, h = insert_depth + 1);
        // two cable-tie slots through the -X ear for USB strain relief
        for (y = [outer_w / 2 - 7.5, outer_w / 2 + 5.5]) translate([-4.5, y, -1]) cube([3.0, 2.0, ear_t + 2]);
    }
    // inside features
    translate([wall, wall, floor_t]) {
        // Micro cradle: two side rails with a ledge the PCB sits on, plus an end stop
        for (y = [micro_y0 - 1.6, micro_y0 + micro_w + gap]) translate([micro_x0 + 4, y, 0]) {
            cube([micro_l - 8, 1.6 - gap / 2, micro_z0 + micro_t + 1.0]);        // rail
        }
        for (y = [micro_y0 - 0.2, micro_y0 + micro_w - 1.4]) translate([micro_x0 + 4, y, 0])
            cube([micro_l - 8, 1.6, micro_z0]);                                    // ledge under PCB edge
        translate([micro_x0 + micro_l + gap, micro_y0 + 3, 0]) cube([1.6, micro_w - 6, micro_z0 + micro_t + 1.5]);   // end stop
        // GY-521 bosses (screws) + a support pad under the opposite edge
        for (i = [0, 1]) translate([gy_x0 + (gy_l - gy_hole_pitch) / 2 + i * gy_hole_pitch, gy_y0 + gy_w - gy_hole_inset, 0])
            difference() { cylinder(d = 5.2, h = gy_standoff); translate([0, 0, -0.1]) cylinder(d = gy_screw_pilot, h = gy_standoff + 1); }
        translate([gy_x0 + 3, gy_y0 + 1.0, 0]) cube([gy_l - 6, 2.0, gy_standoff]);
        // level-shifter bay (5 V Arduino Micro only): low fence, board fixed with VHB tape
        if (use_shifter) translate([sh_x0, sh_y0, 0]) difference() {
            translate([-1.2, -1.2, 0]) cube([shifter_l + 2.4, shifter_w + 2.4, 1.5]);
            cube([shifter_l, shifter_w, 3]);
        }
    }
}

function lug_xy() = [ for (x = [outer_l * 0.2, outer_l * 0.8], y = [-lug_d / 2 + 1, outer_w + lug_d / 2 - 1]) [x, y] ];

module lid() {
    difference() {
        union() {
            rounded_box(outer_l, outer_w, lid_t);
            for (p = lug_xy()) hull() {
                translate([p[0], p[1], 0]) cylinder(d = lug_d, h = lid_t);
                translate([p[0] - lug_d / 2, p[1] < 0 ? 0.5 : outer_w - 1.5, 0]) cube([lug_d, 1, lid_t]);
            }
            // locating lip
            translate([wall + gap / 2, wall + gap / 2, -1.5]) difference() {
                rounded_box(inner_l - gap, inner_w - gap, 1.5, 1);
                translate([1.2, 1.2, -1]) rounded_box(inner_l - gap - 2.4, inner_w - gap - 2.4, 4, 0.5);
            }
            // two ribs that clamp the Micro PCB edges onto its ledges (0.2 mm interference)
            rib_h = inner_h - (micro_z0 + micro_t) + 0.2;
            for (y = [micro_y0 + 0.2, micro_y0 + micro_w - 1.4]) translate([wall + micro_x0 + 12, wall + y, -rib_h])
                cube([micro_l - 24, 1.2, rib_h]);
            // GY-521 hold-down (presses on the board's support-pad edge, clear of the chip)
            gy_rib_h = inner_h - (gy_standoff + gy_t) + 0.2;
            translate([wall + gy_x0 + 3, wall + gy_y0 + 1.0, -gy_rib_h]) cube([gy_l - 6, 1.6, gy_rib_h]);
        }
        // screw holes with counterbores
        for (p = lug_xy()) translate([p[0], p[1], -2]) {
            cylinder(d = lid_screw_d, h = 10);
            translate([0, 0, 2 + lid_t - lid_head_h]) cylinder(d = lid_head_d, h = 5);
        }
        // engraved label (filled by the inlay part on a multi-material printer)
        translate([0, 0, lid_t - 0.6]) text_label(1);
    }
}

module inlay(c = "all") { translate([0, 0, lid_t - 0.6]) text_label(0.6, c); }
module inlay_coloured() { for (k = inlay_colours) color(k == "green" ? "#A6FF4D" : k == "orange" ? "#FF7A29" : "#F3F5F1") inlay(k); }

// ---------------- visual board models (for the fit pictures only, not printed) ------------
// ProMicro pad order from the USB end. -Y edge ("left"): 0.06 0.08 GND GND 0.17 0.20 0.22 0.24 1.00 0.11 1.04 1.06
//                                       +Y edge ("right"): B+ GND RST VCC 0.31 0.29 0.02 1.15 1.13 1.11 0.10 0.09
function pad_x(i) = is_nrf ? (micro_l - 11 * 2.54) / 2 + i * 2.54 : 2.2 + i * 2.54;
module micro_model() { if (is_nrf) nrf_model(); else avr_micro_model(); }
module nrf_model() {                         // origin = PCB corner, PCB bottom at z = 0
    color("DimGray") cube([micro_l, micro_w, micro_t]);
    color("Black") translate([micro_l * 0.55, micro_w / 2, micro_t]) rotate([0, 0, 45]) translate([-3.5, -3.5, 0]) cube([7, 7, 0.9]);  // nRF52840
    color("Silver") translate([-usb_overhang, micro_w / 2 - usb_w / 2, micro_t]) cube([7.3, usb_w, usb_h]);   // USB-C
    for (i = [0 : 11], y = [0.9, micro_w - 0.9])                                                   // 2 x 12 pads
        color("Gold") translate([pad_x(i), y, micro_t]) cylinder(d = 1.6, h = 0.1);
}
module avr_micro_model() {
    color("Teal") cube([micro_l, micro_w, micro_t]);
    color("Black") translate([micro_l * 0.45, micro_w / 2 - 3.5, micro_t]) cube([7, 7, 1]);          // ATmega32U4
    color("Silver") translate([-usb_overhang, micro_w / 2 - usb_w / 2, micro_t]) cube([5.7, usb_w, 2.8]);  // micro-USB
    color("Silver") translate([micro_l * 0.25, 1.5, micro_t]) cube([3, 3, 1.5]);                    // reset button
    for (i = [0 : 16], y = [0.9, micro_w - 0.9])                                                    // 2 x 17 pads
        color("Gold") translate([2.2 + i * 2.54, y, micro_t]) cylinder(d = 1.6, h = 0.1);
}
module gy521_model() {
    difference() {
        color("RoyalBlue") cube([gy_l, gy_w, gy_t]);
        for (i = [0, 1]) translate([(gy_l - gy_hole_pitch) / 2 + i * gy_hole_pitch, gy_w - gy_hole_inset, -1]) cylinder(d = gy_hole_d, h = 5);
    }
    color("Black") translate([gy_l / 2 - 2, gy_w / 2 - 2, gy_t]) cube([4, 4, 0.9]);                // MPU-6050
    for (i = [0 : 7]) color("Gold") translate([gy_l / 2 - 3.5 * 2.54 + i * 2.54, 1.3, gy_t]) cylinder(d = 1.6, h = 0.1);
}
module shifter_model() {
    color("MediumBlue") cube([shifter_l, shifter_w, 1.6]);
    for (i = [0, 1, 2, 3]) color("Black") translate([2 + i * 3, shifter_w / 2 - 1, 1.6]) cube([2, 2, 0.9]);  // BSS138s
    for (i = [0 : 5], x = [0.9, shifter_l - 0.9]) color("Gold") translate([x, 0.9 + i * 1.85, 1.6]) cylinder(d = 1.2, h = 0.1);
}
// a wire through a list of points, as hulled spheres
module wire(pts, c, d = 1.0) { color(c) for (i = [0 : len(pts) - 2]) hull() { translate(pts[i]) sphere(d = d, $fn = 10); translate(pts[i + 1]) sphere(d = d, $fn = 10); } }

M0 = [wall + micro_x0, wall + micro_y0, floor_t + micro_z0];
G0 = [wall + gy_x0, wall + gy_y0, floor_t + gy_standoff];
S0 = [wall + sh_x0, wall + sh_y0, floor_t + 1.0];
module boards(dz = 0) {
    translate(M0 + [0, 0, dz]) micro_model();
    translate(G0 + [0, 0, dz]) gy521_model();
    if (use_shifter) translate(S0 + [0, 0, dz]) shifter_model();
    // M2.5 screws through the GY-521 into the bosses
    for (i = [0, 1]) color("DimGray") translate(G0 + [(gy_l - gy_hole_pitch) / 2 + i * gy_hole_pitch, gy_w - gy_hole_inset, gy_t + dz]) {
        cylinder(d = 4.5, h = 1.6); translate([0, 0, -6]) cylinder(d = 2.5, h = 6);
    }
}
// illustrative wires: Micro pads (+Y edge, USB-far end) -> shifter HV side, shifter LV side -> GY-521 header
wire_cols = ["Red", "Orange", "Black", "DodgerBlue", "LimeGreen"];
module wires(dz = 0) { if (is_nrf) wires_nrf(dz); else wires_avr(dz); }
// nRF board straight to the GY-521: VCC (+Y edge pad 3), GND (-Y pad 2), SDA = 1.00 (-Y pad 8), SCL = 0.11 (-Y pad 9)
// GY-521 header from its first pin: VCC GND SCL SDA XDA XCL AD0 INT
nrf_wires = [ [3, 1, 0, "Red"], [2, 0, 1, "Black"], [9, 0, 2, "Yellow"], [8, 0, 3, "LimeGreen"] ];  // [pad, edge(1=+Y), gy pin, colour]
module wires_nrf(dz = 0) {
    zt = floor_t + micro_z0 + micro_t + 3.2 + dz;
    for (k = [0 : 3]) let(w = nrf_wires[k]) {
        p0 = M0 + [pad_x(w[0]), w[1] ? micro_w - 0.9 : 0.9, micro_t + dz];
        g = G0 + [gy_l / 2 - 3.5 * 2.54 + w[2] * 2.54, 1.3, gy_t + dz];
        ylane = M0[1] + 2.6 + k * 0.9;
        wire([p0, [p0[0], p0[1], zt + k * 0.35], [p0[0] + 1.5, ylane, zt + k * 0.35], [g[0] - 1.5, ylane, zt + k * 0.35],
              [g[0], g[1], g[2] + 2], g], w[3]);
    }
}
module wires_avr(dz = 0) {
    zt = floor_t + micro_z0 + micro_t + 3.5 + dz;
    for (k = [0 : 4]) {
        p0 = M0 + [2.2 + (12 + k) * 2.54, micro_w - 0.9, micro_t + dz];
        s_hv = S0 + [0.9, 0.9 + (k + 1) * 1.85 * 0.9, 1.7 + dz];
        wire([p0, [p0[0], p0[1], zt + k * 0.4], [s_hv[0] - 3, p0[1] - k * 0.6, zt + k * 0.4], [s_hv[0] - 3, s_hv[1], zt - 1], s_hv], wire_cols[k]);
    }
    for (k = [1 : 4]) {
        s_lv = S0 + [shifter_l - 0.9, 0.9 + (k + 1) * 1.85 * 0.9, 1.7 + dz];
        g = G0 + [gy_l / 2 - 3.5 * 2.54 + (k - 1) * 2.54, 1.3, gy_t + dz];
        wire([s_lv, [s_lv[0] + 1.2, s_lv[1], s_lv[2] + 2], [s_lv[0] + 1.2, g[1] - 2.5 - k * 0.5, s_lv[2] + 2.5 + k * 0.4],
              [g[0], g[1] - 2.5 - k * 0.5, s_lv[2] + 2.5 + k * 0.4], [g[0], g[1], g[2] + 2], g], wire_cols[k]);
    }
}

if (part == "base") base();
else if (part == "lid") print_flip() lid();       // printed top face down
else if (part == "inlay") print_flip() inlay();   // all artwork in one body (single inlay colour)
else if (part == "inlay_green" || part == "inlay_orange" || part == "inlay_white")
    print_flip() inlay(part == "inlay_green" ? "green" : part == "inlay_orange" ? "orange" : "white");   // same coordinates as the lid -> load lid + 3 inlays as one object
else if (part == "fit") {                          // boards installed, lid off
    color("SteelBlue") base();
    boards(); wires();
}
else if (part == "none") { }                        // library use (render/render_scene.scad)
else if (part == "lid_preview") {                  // lid as installed, with coloured inlays
    color("#2B302C") lid(); inlay_coloured();
}
else if (part == "mounted") {                      // on a vertical 40x40 profile, box across it
    profile_scene();
}
else if (part == "exploded") {                     // base, boards lifted 18 mm, lid lifted 42 mm
    color("SteelBlue") base();
    boards(18); wires(18);
    color("#2B302C") translate([0, 0, base_h + 42]) lid();
    translate([0, 0, base_h + 42]) inlay_coloured();
}
else {
    color("SteelBlue") base();
    color("#2B302C", 0.9) translate([0, 0, base_h + 12]) lid();
    translate([0, 0, base_h + 12]) inlay_coloured();
    boards();
}

// ---------------- mounting scene: vertical 40x40 slot-8 profile, M6 bolts + T-nuts ------------
module profile40(h) {
    color("Silver") difference() {
        translate([-20, 0, -h / 2]) cube([40, 40, h]);
        for (a = [0, 90, 180, 270]) translate([0, 20, 0]) rotate([0, 0, a]) translate([0, -20, 0]) {
            translate([-4.1, -1, -h]) cube([8.2, 4.5, 2 * h]);       // slot opening
            translate([-8, 2.5, -h]) cube([16, 6, 2 * h]);           // undercut
        }
        translate([0, 20, -h]) cylinder(d = 7, h = 2 * h);
    }
}
module profile_scene() { if (mount_style == "side") side_scene(); else upright_scene(); }

// horizontal 40x40 cross-member, box on its vertical front face, bolts in the horizontal slot
module side_scene() {
    rotate([0, 90, 0]) profile40(240);
    rotate([90, 0, 0]) translate([-outer_l / 2, side_flange / 2 + 1, 0]) {
        color("SteelBlue") base();
        color("#2B302C") translate([0, 0, base_h]) lid();
        translate([0, 0, base_h]) inlay_coloured();
        for (x = [outer_l / 2 - side_pitch / 2, outer_l / 2 + side_pitch / 2]) translate([x, -side_flange / 2 - 1, 0]) {
            color("DimGray") translate([0, 0, plate_t]) cylinder(d = 10, h = 6);
            color("DimGray") translate([0, 0, -9]) cylinder(d = 6, h = plate_t + 9);
            color("Goldenrod") translate([-8, -5, -7]) cube([16, 10, 4]);
        }
    }
}

module upright_scene() {
    profile40(200);
    // box: its bottom on the profile face (y = 0), long axis horizontal, bolt line along the profile
    rotate([90, 0, 0]) translate([-outer_l / 2, -outer_w / 2, 0]) {
        color("SteelBlue") base();
        color("#2B302C") translate([0, 0, base_h]) lid();
        translate([0, 0, base_h]) inlay_coloured();
        for (y = [-bolt_off, outer_w + bolt_off]) translate([outer_l / 2, y, 0]) {
            color("DimGray") translate([0, 0, plate_t]) cylinder(d = 10, h = 6);                 // M6 head
            color("DimGray") translate([0, 0, -9]) cylinder(d = 6, h = plate_t + 9);             // shank
            color("Goldenrod") translate([-5, -8, -7]) cube([10, 16, 4]);                        // T-nut in the slot
        }
    }
}

// Rotate (not mirror!) 180° about X so the top face lies on the bed and text stays readable.
module print_flip() translate([0, outer_w, lid_t]) rotate([180, 0, 0]) children();

echo(str("Outer box: ", outer_l, " x ", outer_w, " x ", base_h + lid_t, " mm; mount ", mount_style, ": ",
         mount_style == "side" ? str("M6 bolt pitch ", side_pitch, " mm along the profile, bolt line ", side_flange / 2 + 1, " mm below the box side")
       : mount_style == "across" ? str("M6 bolt pitch ", outer_w + 2 * bolt_off, " mm along the profile")
       : str("M6 slot centre distance ", outer_l + ear_l + 2, " mm along the box")));
