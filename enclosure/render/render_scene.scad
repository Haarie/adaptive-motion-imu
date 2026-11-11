// Photo-render scene: the IMU box on a horizontal 40x40 cross-member with real hardware.
// Each "rpart" is exported as its own STL so Blender can give it its own material:
//   openscad -D 'rpart="bolts"' -o build/bolts.stl render_scene.scad   (see render.sh)
include <../adaptive_motion_imu_enclosure.scad>

rpart = "all";   // profile | base | lid | inlay_green | inlay_orange | inlay_white | bolts | washers | tnuts | lidscrews | cable | plug | all
$fn = 64;

// ---------------- hardware ----------------------------------------------------------------
// DIN 912 M6 x len socket head cap screw, head bottom at z = 0, shank along -Z
module din912_m6(len = 16) {
    difference() {
        union() {
            hull() { cylinder(d = 10, h = 5.5); translate([0, 0, 5.5]) cylinder(d = 9.2, h = 0.5); }   // head, chamfered top
            translate([0, 0, -0.6]) cylinder(d1 = 6, d2 = 6.6, h = 0.6);                              // fillet under head
            translate([0, 0, -len + 1]) cylinder(d = 5.0, h = len - 1);                               // thread core
            for (i = [0 : len - 2]) translate([0, 0, -len + 1 + i])                                   // thread crests (pitch 1)
                { cylinder(d1 = 5.0, d2 = 6.0, h = 0.5, $fn = 32); translate([0, 0, 0.5]) cylinder(d1 = 6.0, d2 = 5.0, h = 0.5, $fn = 32); }
            translate([0, 0, -len]) cylinder(d1 = 4.4, d2 = 5.0, h = 1);                              // chamfered tip
        }
        translate([0, 0, 2.6]) cylinder(d = 5 / cos(30), h = 5, $fn = 6);                           // hex socket AF5
        translate([0, 0, 1.6]) cylinder(d1 = 0, d2 = 5.6, h = 1.01, $fn = 6);                        // drill-point bottom
    }
}
// DIN 125 M6 washer, bottom at z = 0
module din125_m6() difference() { cylinder(d = 12, h = 1.6); translate([0, 0, -1]) cylinder(d = 6.4, h = 4); }
// slot-8 drop-in T-nut (40 series), top of the neck at z = 0 (= profile face), slot along X
module tnut_slot8() {
    difference() {
        union() {
            translate([0, 0, -6.6]) hull() {                                     // body under the slot lips
                translate([-5.5, -6.8, 0.6]) cube([11, 13.6, 3.0]);
                translate([-5.5, -5.2, 0]) cube([11, 10.4, 3.6]);
            }
            translate([-5.5, -3.8, -3.2]) cube([11, 7.6, 2.6]);                   // neck in the slot opening
        }
        translate([0, 0, -10]) cylinder(d = 5.0, h = 12);                       // M6 thread (minor dia)
        translate([0, 0, -0.9]) cylinder(d1 = 5, d2 = 6.4, h = 0.91);           // countersink
    }
}
// ISO 7380 M3 button head, head bottom at z = 0
module iso7380_m3() {
    difference() {
        intersection() { scale([1, 1, 1.65 / 2.85]) sphere(d = 5.7 * 1.0, $fn = 48); translate([-5, -5, 0]) cube([10, 10, 3]); }
        translate([0, 0, 0.55]) cylinder(d = 2 / cos(30), h = 3, $fn = 6);     // hex AF2
    }
    cylinder(d = 5.7, h = 0.3);
}

// ---------------- USB-C plug + cable --------------------------------------------------------
usb_c = [wall + micro_y0 + micro_w / 2, floor_t + micro_z0 + micro_t + usb_h / 2];  // [y, z] of the receptacle (box coords)
module usb_plug() {   // box coordinates, plug enters the -X wall
    translate([0, usb_c[0], usb_c[1]]) {
        translate([-2.0, 0, 0]) rotate([0, 90, 0]) hull() for (s = [-1, 1]) translate([0, s * 2.9, 0]) cylinder(d = 2.5, h = 6.5, $fn = 24);  // metal shell (in the receptacle)
        translate([-18, 0, 0]) rotate([0, 90, 0]) hull() for (s = [-1, 1]) translate([0, s * 3.4, 0]) cylinder(d = 6.0, h = 16, $fn = 32);      // overmould
        translate([-24, 0, 0]) rotate([0, 90, 0]) cylinder(d1 = 4.2, d2 = 5.5, h = 6.2);                                                          // strain relief
    }
}
function bez(p0, p1, p2, t) = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;
module usb_cable() {
    p0 = [-24, usb_c[0], usb_c[1]];
    p1 = [-46, usb_c[0], usb_c[1]];
    p2 = [-46, usb_c[0] - 30, usb_c[1] - 4];
    pts = concat([ for (i = [0 : 24]) bez(p0, p1, p2, i / 24) ], [[-46, usb_c[0] - 110, usb_c[1] - 4]]);
    for (i = [0 : len(pts) - 2]) hull() { translate(pts[i]) sphere(d = 4.2, $fn = 20); translate(pts[i + 1]) sphere(d = 4.2, $fn = 20); }
}

// ---------------- scene in world coordinates (same placement as part = "mounted") ------------
module box_frame() rotate([90, 0, 0]) translate([-outer_l / 2, side_flange / 2 + 1, 0]) children();
bolt_xy = [ for (x = [outer_l / 2 - side_pitch / 2, outer_l / 2 + side_pitch / 2]) [x, -side_flange / 2 - 1] ];

if (rpart == "profile" || rpart == "all") rotate([0, 90, 0]) profile40(170);
if (rpart == "base" || rpart == "all") box_frame() base();
if (rpart == "lid" || rpart == "all") box_frame() translate([0, 0, base_h]) lid();
for (c = ["green", "orange", "white"]) if (rpart == str("inlay_", c) || rpart == "all") box_frame() translate([0, 0, base_h]) inlay(c);
if (rpart == "washers" || rpart == "all") box_frame() for (p = bolt_xy) translate([p[0], p[1], plate_t]) din125_m6();
if (rpart == "bolts" || rpart == "all") box_frame() for (p = bolt_xy) translate([p[0], p[1], plate_t + 1.6]) din912_m6(16);
if (rpart == "tnuts" || rpart == "all") box_frame() for (p = bolt_xy) translate([p[0], p[1], 0]) tnut_slot8();
if (rpart == "lidscrews" || rpart == "all") box_frame() for (p = lug_xy()) translate([p[0], p[1], base_h + lid_t - lid_head_h]) iso7380_m3();
if (rpart == "plug" || rpart == "all") box_frame() usb_plug();
if (rpart == "cable" || rpart == "all") box_frame() usb_cable();
