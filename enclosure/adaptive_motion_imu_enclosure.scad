// AdaptiveMotionIMU enclosure — Arduino Micro (headerless) + GY-521 (MPU-6050)
// Rigid two-part box that bolts to aluminium extrusion (2020/3030/4040 T-nuts, M5) or flat steel.
// Design rule: the sensor must move EXACTLY like the seat. So: thick floor, sensor screwed to
// bosses that grow out of the floor, ears bolted with two M5 bolts, print in PETG/ASA, 100 % infill
// around the sensor pocket (or simply 6 perimeters + 40 % gyroid everywhere).
//
// Render parts:  openscad -D 'part="base"'  -o base.stl  adaptive_motion_imu_enclosure.scad
//                openscad -D 'part="lid"'   -o lid.stl   ...
//                openscad -D 'part="inlay"' -o lid_inlay.stl ...   (second colour on the MMU3)
//                openscad -D 'part="assembly"' -o preview.stl ...
//
// MEASURE YOUR BOARDS WITH CALIPERS and adjust the "board" section — clone boards vary ±0.5 mm.

part = "assembly";      // base | lid | inlay | assembly
$fn = 48;

// ---------------- boards ------------------------------------------------------------------
micro_l = 48.3;  micro_w = 17.8;  micro_t = 1.6;     // Arduino Micro PCB
micro_under = 2.6;      // tallest part on the underside (headerless board: reset/ICSP pads)
micro_over  = 4.2;      // tallest part on top (USB receptacle / crystal)
usb_w = 8.0;  usb_h = 3.2;  usb_overhang = 0.8;      // micro-B receptacle, sticks out past PCB edge

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

inner_l = usb_overhang + 0.6 + micro_l + gap + 4 + gy_l + 3 + shifter_l + gap + 1;
inner_w = max(micro_w, gy_w, shifter_w) + 2 * gap + 4.0;
inner_h = micro_under + micro_t + micro_over + 3.0;
outer_l = inner_l + 2 * wall;
outer_w = inner_w + 2 * wall;
base_h  = floor_t + inner_h;

// mounting ears (M5, 20 mm-pitch-agnostic: two bolts along the extrusion)
ear_l = 14;  ear_t = 5.0;  m5_d = 5.5;  m5_cb_d = 9.6;  m5_cb_h = 0;   // set cb_h>0 for socket heads
ear_slot = 4;           // slot length to absorb hole-position tolerance

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

module text_label(depth) {
    // arrow + text on the lid, pointing to rig front (+X of the box)
    translate([outer_l / 2 - 4, outer_w / 2, 0]) linear_extrude(depth) {
        translate([-20, 0]) text("FWD", size = 6, font = "DejaVu Sans:style=Bold", halign = "center", valign = "center");
        translate([4, 0]) polygon([[0, -2], [14, -2], [14, -4.5], [21, 0], [14, 4.5], [14, 2], [0, 2]]);
    }
    translate([outer_l - 10, outer_w / 2, 0]) linear_extrude(depth)
        text("UP", size = 3.2, font = "DejaVu Sans:style=Bold", halign = "center", valign = "center");
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
            // ears
            // -X ear stays flat so the USB plug clears it; +X ear gets a stiffening gusset
            translate([-ear_l, 0, 0]) cube([ear_l + 1, outer_w, ear_t]);
            translate([outer_l - 1, 0, 0]) hull() {
                cube([ear_l + 1, outer_w, ear_t]);
                cube([1, outer_w, ear_t + 5]);
            }
        }
        // cavity
        translate([wall, wall, floor_t]) rounded_box(inner_l, inner_w, inner_h + 1, 1);
        // M5 slots in the ears
        for (x = [-ear_l / 2 - 2, outer_l + ear_l / 2]) translate([x, outer_w / 2, -1]) {
            hull() for (dx = [-ear_slot / 2, ear_slot / 2]) translate([dx, 0, 0]) cylinder(d = m5_d, h = ear_t + 10);
            if (m5_cb_h > 0) translate([0, 0, ear_t - m5_cb_h + 1]) hull() for (dx = [-ear_slot / 2, ear_slot / 2]) translate([dx, 0, 0]) cylinder(d = m5_cb_d, h = 20);
        }
        // USB opening in the -X wall, centred on the Micro's receptacle (room for the plug overmould)
        translate([-1, wall + micro_y0 + micro_w / 2 - 5.5, floor_t + micro_z0 + micro_t - 2.0]) cube([wall + 2, 11, usb_h + 4.5]);
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
        // level-shifter bay: low fence, board fixed with VHB tape
        translate([sh_x0, sh_y0, 0]) difference() {
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

module inlay() { translate([0, 0, lid_t - 0.6]) text_label(0.6); }

if (part == "base") base();
else if (part == "lid") print_flip() lid();       // printed top face down
else if (part == "inlay") print_flip() inlay();   // same coordinates as the lid -> load both as one object
else {
    color("SteelBlue") base();
    color("Gainsboro", 0.6) translate([0, 0, base_h + 12]) lid();
    color("Orange") translate([0, 0, base_h + 12]) inlay();
    // ghost boards
    color("Teal", 0.8) translate([wall + micro_x0, wall + micro_y0, floor_t + micro_z0]) cube([micro_l, micro_w, micro_t]);
    color("DarkBlue", 0.8) translate([wall + gy_x0, wall + gy_y0, floor_t + gy_standoff]) cube([gy_l, gy_w, gy_t]);
}

// Rotate (not mirror!) 180° about X so the top face lies on the bed and text stays readable.
module print_flip() translate([0, outer_w, lid_t]) rotate([180, 0, 0]) children();

echo(str("Outer box: ", outer_l, " x ", outer_w, " x ", base_h + lid_t, " mm; with ears: ", outer_l + 2 * ear_l,
         " mm; M5 slot centre distance: ", outer_l + ear_l + 2, " mm"));
